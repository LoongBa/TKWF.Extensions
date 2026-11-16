using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Captcha.Tests;

/// <summary>
/// CaptchaExtensionInitializer 接线测试——[TKWFExtension("Captcha")] 特性声明、DI 注册形态完整
/// （ICaptchaService AddConstructibleService 守卫工厂 + 实现类 throw-factory + 帧外解析抛、
/// ICaptchaProvider TryAddEnumerableConstructible 集合版守卫工厂、Options/IRateLimitCheck/IMemoryCache
/// fallback TryAdd 首注册胜出、挑战态 Store Singleton）、白名单声明（V4.9.85 ADR47）。
/// </summary>
public class CaptchaInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(CaptchaExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Captcha", attr!.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_ICaptchaService_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // AddConstructibleService——接口注册为构造工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICaptchaService));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_CaptchaService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<ICaptchaService>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(CaptchaService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<DomainGuardException>(() => provider.GetRequiredService<CaptchaService>());
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // 接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<DomainGuardException>(() => provider.GetRequiredService<ICaptchaService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ICaptchaService", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_ImageCaptchaProvider_TryAddEnumerableConstructible()
    {
        var services = new ServiceCollection();
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 多实现集合（V4.10.55 ADR92：TryAddEnumerableConstructible——集合版守卫工厂，
        // 工厂委托形态：ImplementationType=null + ImplementationFactory 非空）
        var descriptors = services.Where(d => d.ServiceType == typeof(ICaptchaProvider)).ToList();

        Assert.Single(descriptors);
        Assert.All(descriptors, d => Assert.Null(d.ImplementationType));
        Assert.All(descriptors, d => Assert.NotNull(d.ImplementationFactory));
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }

    [Fact]
    public void ConfigureServices_TryAddEnumerableConstructible_DoesNotOverrideConsumerProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICaptchaProvider>(_ => throw new NotSupportedException("consumer marker"));
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // TryAddEnumerableConstructible 不覆盖不同实现——消费方自定义 Provider + 内置 image 共存
        var descriptors = services.Where(d => d.ServiceType == typeof(ICaptchaProvider)).ToList();

        Assert.Equal(2, descriptors.Count);
        Assert.All(descriptors, d => Assert.NotNull(d.ImplementationFactory));
    }

    [Fact]
    public void ConfigureServices_Registers_Options_Binding()
    {
        var services = new ServiceCollection();
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // AddOptions<CaptchaOptions>() 注册开放式泛型 IOptions<>（守卫工厂 ActivatorUtilities 兜底可解析）；
        // 配置节（TKWF:Captcha）绑定归消费方 SG1 [Options] 编译期生成——本初始化器不注册 IConfigureOptions
        //（对比 MFA BindConfiguration 形态；测试以显式 Options.Create 为准）。
        var openDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOptions<>));
        Assert.NotNull(openDescriptor);
        var configureDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IConfigureOptions<CaptchaOptions>));
        Assert.Null(configureDescriptor);

        var provider = services.BuildServiceProvider();
        var defaults = provider.GetRequiredService<IOptions<CaptchaOptions>>().Value;
        Assert.True(defaults.Enabled);
        Assert.Equal("image", defaults.EnabledProviderType);
        Assert.Equal(300, defaults.ChallengeTtlSeconds);
        Assert.Equal(3, defaults.AttemptsPerChallenge);
        Assert.Equal(10, defaults.IssuePerMinutePerIp);
    }

    [Fact]
    public void ConfigureServices_Registers_RateLimitCheck_Fallback_MemoryRateLimitCheck()
    {
        var services = new ServiceCollection();
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

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
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        Assert.Same(sentinel, provider.GetRequiredService<IRateLimitCheck>());
    }

    [Fact]
    public void ConfigureServices_Registers_MemoryCache_Fallback()
    {
        var services = new ServiceCollection();
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 挑战态存储底座（IMemoryCache）TryAddSingleton fallback——消费方可覆盖（多实例共享挑战态）
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMemoryCache));
        Assert.NotNull(descriptor);
        Assert.Equal(typeof(MemoryCache), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_ChallengeStateStore_Singleton()
    {
        var services = new ServiceCollection();
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 挑战态存储（internal 基础设施单例——per-challengeId 锁表跨门面/Provider 实例共享）
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(CaptchaChallengeStateStore));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact]
    public async Task Host_Di_Resolves_ICaptchaService_ProductionAopPath()
    {
        using var host = CaptchaTestHost.Create();

        // 帧内 User.Use<ICaptchaService>() AOP 可解析（守卫工厂——CurrentAopUser = 真实 DomainUser）；
        // 实现类直解抛（throw-factory）；门面真实可用（Issue 正演）
        TestHostAssert.AssertConstructible<ICaptchaService>(host.User);
        TestHostAssert.AssertImplementationThrows<CaptchaService>(host.Services);

        var challenge = await host.Captcha.IssueAsync("login", TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(challenge.ChallengeId));
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
        Assert.Equal(typeof(CaptchaExtensionInitializer<>), attr!.InitializerType);
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