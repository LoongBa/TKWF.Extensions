using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.SmsCode.Tests;

/// <summary>
/// SmsCodeExtensionInitializer 接线测试（方案 §7 装配行）——[TKWFExtension("SmsCode")] 特性声明、
/// Captcha.Abstractions 可空依赖声明（[TKWFExtensionDependency]，P1-5 评审闭环）、DI 注册形态完整
/// （ISmsCodeService AddConstructibleService 守卫工厂 + 实现类 throw-factory + 帧外解析抛、
/// ISmsCodeScopeResolver TryAdd 默认空串、IRateLimitCheck fallback TryAdd 首注册胜出、
/// Options AddOptions 兜底）、白名单声明（V4.9.85 ADR47）。
/// </summary>
public class SmsCodeInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(SmsCodeExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("SmsCode", attr!.Name);
    }

    [Fact]
    public void ExtensionDependency_Declared_CaptchaAbstractions_Optional()
    {
        // P1-5 闭环：Captcha.Abstractions 可空依赖声明（方案 §4.2，AuthCenter⇄Emailing.Abstractions 先例）
        var attr = typeof(SmsCodeExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionDependencyAttribute), false)
            .Cast<TKWFExtensionDependencyAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(typeof(TKWF.Ext.Captcha.ICaptchaService), attr!.DependencyType);
        // 预发布下限（Abstractions 未打 tag 本地构建 0.1.0-preview.x；正式 v0.1.0 后仍满足——Federation 先例）
        Assert.Equal("0.1.0-preview.0", attr.MinVersion);
    }

    [Fact]
    public void ConfigureServices_Registers_ISmsCodeService_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new SmsCodeExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // AddConstructibleService——接口注册为构造工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISmsCodeService));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_SmsCodeService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new SmsCodeExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<ISmsCodeService>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(SmsCodeService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<DomainGuardException>(() => provider.GetRequiredService<SmsCodeService>());
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // 接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new SmsCodeExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<DomainGuardException>(() => provider.GetRequiredService<ISmsCodeService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ISmsCodeService", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_ScopeResolver_Default_Empty()
    {
        var services = new ServiceCollection();
        new SmsCodeExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // TryAddSingleton<ISmsCodeScopeResolver, EmptyScopeResolver>——无租户消费方默认空串（方案 §4.5）；
        // 租户消费方 TryAdd Scoped 覆盖；internal 嵌套类经 IVT 可达
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISmsCodeScopeResolver));
        Assert.NotNull(descriptor);
        Assert.Equal(typeof(SmsCodeExtensionInitializer<TestUserInfo>.EmptyScopeResolver), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        var provider = services.BuildServiceProvider();
        Assert.Equal("", provider.GetRequiredService<ISmsCodeScopeResolver>().GetScopePrefix());
    }

    [Fact]
    public void ConfigureServices_TryAdd_RateLimitCheck_Fallback()
    {
        var services = new ServiceCollection();
        new SmsCodeExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // TryAddSingleton fallback——频控"始终在"（fail-closed）；消费方显式注册时静默替换
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRateLimitCheck));
        Assert.NotNull(descriptor);
        Assert.Equal(typeof(MemoryRateLimitCheck), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        var provider = services.BuildServiceProvider();
        Assert.IsType<MemoryRateLimitCheck>(provider.GetRequiredService<IRateLimitCheck>());
    }

    [Fact]
    public void ConfigureServices_TryAdd_RateLimitCheck_ConsumerExplicitWins()
    {
        var services = new ServiceCollection();

        // 消费方显式注册在先（如 SQL 跨实例频控）→ TryAddSingleton 不覆盖（首注册胜出）
        var sentinel = new SentinelRateLimitCheck();
        services.AddSingleton<IRateLimitCheck>(sentinel);
        new SmsCodeExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        Assert.Same(sentinel, provider.GetRequiredService<IRateLimitCheck>());
    }

    [Fact]
    public void ConfigureServices_Registers_Options_Binding()
    {
        var services = new ServiceCollection();
        new SmsCodeExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // AddOptions<SmsCodeOptions>() 注册开放式泛型 IOptions<>（守卫工厂 ActivatorUtilities 兜底可解析）；
        // 配置节（TKWF:SmsCode）绑定归消费方 SG1 [Options] 编译期生成——本初始化器不注册 IConfigureOptions
        var openDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOptions<>));
        Assert.NotNull(openDescriptor);
        var configureDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IConfigureOptions<SmsCodeOptions>));
        Assert.Null(configureDescriptor);

        var provider = services.BuildServiceProvider();
        var defaults = provider.GetRequiredService<IOptions<SmsCodeOptions>>().Value;
        Assert.Equal(60, defaults.SmsResendIntervalSeconds);
        Assert.Equal(6, defaults.CodeLength);
        // 默认场景注册表：login 要求 CAPTCHA 门禁（M3 接线完成态）
        Assert.True(defaults.Scenes[SmsScenes.Login].RequiresCaptcha);
    }

    [Fact]
    public async Task Host_Di_Resolves_ISmsCodeService_ProductionAopPath()
    {
        using var host = SmsCodeTestHost.Create();

        // 帧内 User.Use<ISmsCodeService>() AOP 可解析（守卫工厂——CurrentAopUser = 真实 DomainUser）；
        // 实现类直解抛（throw-factory）
        TestHostAssert.AssertConstructible<ISmsCodeService>(host.User);
        TestHostAssert.AssertImplementationThrows<SmsCodeService>(host.Services);

        // 门面真实可用：守卫工厂产物为 SmsCodeService 实现
        var svc = host.User.Use<ISmsCodeService>();
        Assert.IsType<SmsCodeService>(svc);
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
        Assert.Equal(typeof(SmsCodeExtensionInitializer<>), attr!.InitializerType);
    }
}

/// <summary>
/// 消费方显式注册哨兵实现——验证 TryAddSingleton 首注册胜出（消费方显式注册为确定性路径）。
/// </summary>
internal sealed class SentinelRateLimitCheck : IRateLimitCheck
{
    public bool TryAcquire(string key, int maxAttempts, TimeSpan window, out int remaining)
    {
        remaining = maxAttempts;
        return true;
    }

    public TimeSpan GetRetryAfter(string key, TimeSpan window) => TimeSpan.Zero;

    public int GetRemaining(string key, int maxAttempts, TimeSpan window) => maxAttempts;
}