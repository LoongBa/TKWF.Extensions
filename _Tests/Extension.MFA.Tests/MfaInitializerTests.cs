using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// MFAExtensionInitializer 接线测试——[TKWFExtension("MFA")] 特性声明、DI 注册完整
/// （IMfaService AddConstructibleService 守卫工厂 + 实现类 throw-factory + 域外解析抛、
/// 双 IMfaMethod TryAddEnumerable——Oracle C5 防 SMS 静默丢失、Options 绑定）、白名单声明（V4.9.85 ADR47）。
/// <para>V4.10.53（领域自治根治）：注册形态由 TryAddScoped 改为 <c>AddConstructibleService</c>——
/// 消费方统一经 <c>User.Use&lt;IMfaService&gt;()</c> 解析（AOP 路径先设 CurrentAopUser 再 GetRequiredService）。</para>
/// </summary>
public class MfaInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(MFAExtensionInitializer<MfaUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("MFA", attr!.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_IMfaService_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new MFAExtensionInitializer<MfaUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口注册为构造工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMfaService));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_MfaService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new MFAExtensionInitializer<MfaUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<IMfaService>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(MfaService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<MfaService>());
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // V4.10.53：接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new MFAExtensionInitializer<MfaUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IMfaService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IMfaService", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_TwoMfaMethods_TryAddEnumerable()
    {
        var services = new ServiceCollection();
        new MFAExtensionInitializer<MfaUserInfo>().ConfigureServices(services);

        // 双 IMfaMethod 实现（TryAddEnumerable——同 ServiceType 多实现并存，Oracle C5）
        var descriptors = services.Where(d => d.ServiceType == typeof(IMfaMethod)).ToList();

        Assert.Equal(2, descriptors.Count);
        Assert.Contains(descriptors, d => d.ImplementationType == typeof(TotpMfaMethod));
        Assert.Contains(descriptors, d => d.ImplementationType == typeof(SmsMfaMethod));
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }

    [Fact]
    public void ConfigureServices_TryAddEnumerable_DoesNotOverrideConsumerMethod()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMfaMethod>(_ => throw new NotSupportedException("consumer marker"));
        new MFAExtensionInitializer<MfaUserInfo>().ConfigureServices(services);

        // TryAddEnumerable 不覆盖不同实现——消费方自定义方法 + 内置双方法共存
        var descriptors = services.Where(d => d.ServiceType == typeof(IMfaMethod)).ToList();

        Assert.Equal(3, descriptors.Count);
        Assert.Contains(descriptors, d => d.ImplementationType == typeof(TotpMfaMethod));
        Assert.Contains(descriptors, d => d.ImplementationType == typeof(SmsMfaMethod));
    }

    [Fact]
    public void ConfigureServices_Registers_Options_Binding()
    {
        var services = new ServiceCollection();
        new MFAExtensionInitializer<MfaUserInfo>().ConfigureServices(services);

        // AddOptions<MfaOptions>() 注册开放式泛型 IOptions<>（UnnamedOptionsManager）+ IConfigureOptions
        // （BindConfiguration 的配置动作）；闭合 IOptions<MfaOptions> 由 Options 解析时物化。
        var openDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOptions<>));
        Assert.NotNull(openDescriptor);
        var configureDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IConfigureOptions<MfaOptions>));
        Assert.NotNull(configureDescriptor);
    }

    [Fact]
    public void Host_Di_Resolves_MfaService_WithTwoMethods_And_DefaultOptions()
    {
        using var host = MfaTestHost.Create();

        // IMfaService 可经生产 AOP 路径解析（守卫工厂——CurrentAopUser = 真实 DomainUser）
        Assert.NotNull(host.Mfa);

        // IEnumerable<IMfaMethod> 恰 2 实现（TryAddEnumerable——C5 防 SMS 静默丢失）；注册序 totp → sms
        var methods = host.GetRequiredService<System.Collections.Generic.IEnumerable<IMfaMethod>>().ToList();
        Assert.Equal(2, methods.Count);
        Assert.Equal(new[] { "totp", "sms" }, methods.Select(m => m.Method).ToArray());

        // Options 默认值（方案 §四决策 7 全表）
        var options = host.GetRequiredService<IOptions<MfaOptions>>().Value;
        Assert.Equal(300, options.ChallengeTtlSeconds);
        Assert.Equal(30, options.TotpTimeStepSeconds);
        Assert.Equal(6, options.TotpDigits);
        Assert.Equal(1, options.TotpClockSkewWindows);
        Assert.Equal(5, options.MaxVerifyAttemptsPerWindow);
        Assert.Equal(5, options.VerifyAttemptWindowMinutes);
        Assert.Equal(5, options.SmsMaxPerHour);
        Assert.Equal(8, options.RecoveryCodeCount);
    }

    [Fact]
    public void Host_Registers_RecordingSmsSender_ByDefault()
    {
        using var host = MfaTestHost.Create();

        // 测试宿主默认注册记录型 IMfaSmsSender——SMS 方法惰性解析不抛 MfaMockForbiddenException
        var sender = host.GetRequiredService<IMfaSmsSender>();
        Assert.IsType<RecordingMfaSmsSender>(sender);
    }

    [Fact]
    public void ConsumerHostInitializer_Declares_EnabledExtensionWhitelist()
    {
        var attr = typeof(ConsumerHostInitializer)
            .GetCustomAttributes(typeof(TKWFEnabledExtensionAttribute), false)
            .Cast<TKWFEnabledExtensionAttribute>()
            .FirstOrDefault();

        // V4.9.85 (ADR47)：消费方白名单声明——发现不自动启用，须显式声明三钩子才接线
        Assert.NotNull(attr);
        Assert.Equal(typeof(MFAExtensionInitializer<>), attr!.InitializerType);
    }
}
