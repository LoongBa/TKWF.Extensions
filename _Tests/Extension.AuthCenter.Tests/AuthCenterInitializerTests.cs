using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// AuthCenterExtensionInitializer 测试——[TKWFExtension] 特性声明、DI 注册形态（V4.10.53 领域自治根治三态）。
/// <para>V4.10.53（领域自治根治，ADR90，正确路线）注册形态：</para>
/// <list type="bullet">
/// <item>10 门面（接口 : IDomainService）→ <c>AddConstructibleService</c>：接口 = 可构造守卫工厂
///     （非实现映射）+ 实现类 = throw-factory；域作用域外（无 CurrentAopUser）直接 DI 解析接口必抛领域架构守卫；</item>
/// <item><see cref="IAuthenticationProvider"/> 双实现 → V4.10.55（ADR92）<c>TryAddEnumerableConstructible</c>
///     （集合版守卫工厂——工厂委托形态 + 帧内 CurrentAopUser 供给；帧外枚举抛守卫）。</item>
/// <item><see cref="ISmsLoginService"/>/<see cref="IWechatLoginService"/> 登录编排门面（V4.10.55 ADR92/T3 闭环）→
///     <c>AddConstructibleService</c>——表现层经 <c>User.Use&lt;门面&gt;()</c> 帧内编排 Provider 集合。</item>
/// </list>
/// </summary>
public class AuthCenterInitializerTests
{
    /// <summary>12 个 AddConstructibleService 门面（接口 → 实现）+ 2 登录编排门面（V4.10.55 ADR92/T3 闭环）。</summary>
    private static readonly (System.Type Interface, System.Type Impl)[] Facades =
    [
        (typeof(ITokenService), typeof(TokenService)),
        (typeof(IAuthLoginAttemptService), typeof(AuthLoginAttemptService)),
        (typeof(IOAuthTicketService), typeof(OAuthTicketService)),
        (typeof(ISmsVerificationService), typeof(SmsVerificationService)),
        (typeof(IPlatformCredentialService), typeof(PlatformCredentialService)),
        (typeof(IPlatformAccountMapService), typeof(PlatformAccountMapService)),
        (typeof(IWeChatApiClient), typeof(WeChatApiClient)),
        (typeof(ITokenVerifier), typeof(LocalJwtTokenVerifier)),
        (typeof(IAuthAccountQueryService), typeof(AuthAccountQueryService)),
        (typeof(IAuthAccountService), typeof(AuthAccountService)),
        (typeof(ISmsLoginService), typeof(SmsLoginService)),
        (typeof(IWechatLoginService), typeof(WechatLoginService))
    ];

    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(AuthCenterExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("AuthCenter", attr!.Name);
    }

    /// <summary>V4.10.53：10 门面接口注册为可构造守卫工厂（ImplementationFactory，非实现映射）+ Scoped。</summary>
    [Fact]
    public void ConfigureServices_Registers_FacadeInterfaces_GuardFactory()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        foreach (var (interfaceType, _) in Facades)
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == interfaceType);
            Assert.NotNull(descriptor);
            Assert.Null(descriptor!.ImplementationType);      // 非实现映射——可构造守卫工厂
            Assert.NotNull(descriptor.ImplementationFactory);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }
    }

    /// <summary>V4.10.53：12 门面实现类注册为 throw-factory——禁止直接 DI 解析（必须经 User.Use&lt;接口&gt;()）。</summary>
    [Fact]
    public void ConfigureServices_Registers_Implementations_ThrowFactory()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        foreach (var (_, implType) in Facades)
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == implType);
            Assert.NotNull(descriptor);
            Assert.NotNull(descriptor!.ImplementationFactory);
        }

        var provider = services.BuildServiceProvider();
        foreach (var (_, implType) in Facades)
        {
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService(implType));
        }
    }

    /// <summary>AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use&lt;T&gt;() 调用链内解析接口必抛（DI004 运行期兜底）。</summary>
    [Fact]
    public void InterfaceFactories_OutsideUseScope_Throw()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ITokenService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ITokenService", ex.Message);
    }

    /// <summary>多 Provider——V4.10.55（ADR92）改 TryAddEnumerableConstructible（集合版守卫工厂）：双实现以工厂委托形态注册（ImplementationType=null + ImplementationFactory 非空）。</summary>
    [Fact]
    public void ConfigureServices_Registers_TwoProviders_Enumerable()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IAuthenticationProvider)).ToList();
        Assert.Equal(3, descriptors.Count);   // V0.9.0（B.9）：+PasswordAuthenticationProvider（短信 + 微信 + 密码）
        // TryAddEnumerableConstructible：工厂委托形态（ImplementationType=null + ImplementationFactory 非空）——
        // 帧内（User.Use 链）经 CurrentAopUser 供给构造；实现类 SmsAuthenticationProvider 另有 throw-factory 描述符
        Assert.DoesNotContain(descriptors, d => d.ImplementationType != null);
        Assert.All(descriptors, d => Assert.NotNull(d.ImplementationFactory));
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }

    /// <summary>实现类 throw-factory（禁直接 DI 直取——须经 User.Use&lt;接口&gt;() 帧内集合枚举创建）。</summary>
    [Fact]
    public void ConfigureServices_ProviderImpl_ThrowFactory()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        foreach (var implType in new[] { typeof(SmsAuthenticationProvider), typeof(WeChatAuthenticationProvider) })
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == implType);
            Assert.NotNull(descriptor);
            Assert.NotNull(descriptor!.ImplementationFactory);
            Assert.Throws<InvalidOperationException>(() =>
            {
                var sp = services.BuildServiceProvider();
                _ = sp.GetRequiredService(implType);
            });
        }

        // 登录编排门面（V4.10.55 ADR92/T3 闭环）：AddConstructibleService 守卫工厂注册
        foreach (var facade in new[] { typeof(ISmsLoginService), typeof(IWechatLoginService) })
        {
            Assert.Contains(services, d => d.ServiceType == facade);
        }
    }

    /// <summary>⚠️ D14 核心验收：零 DataService 手动注册（ADR61 铁律）——Initializer 不得注册任何 *EntityDataService。</summary>
    [Fact]
    public void ConfigureServices_ZeroDataServiceManualRegistration()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var dataServiceDescriptors = services
            .Where(d => d.ServiceType.Name.EndsWith("DataService", System.StringComparison.Ordinal))
            .ToList();
        Assert.Empty(dataServiceDescriptors);
    }

    /// <summary>消费方覆盖语义（V4.10.53）：扩展钩子先于消费方 OnRegisterDomainServices——消费方 AddScoped 后注册覆盖守卫工厂（MS DI 后注册者胜）。</summary>
    [Fact]
    public void ConfigureServices_ConsumerRegisteredAfter_OverridesGuardFactory()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddScoped<ITokenService, ConsumerTokenService>(); // 消费方 OnRegisterDomainServices（扩展钩子之后）

        var descriptors = services.Where(d => d.ServiceType == typeof(ITokenService)).ToList();
        // AddConstructibleService 接口守卫工厂 + 消费方 AddScoped = 2 描述符；解析取最后一个（消费方实现）
        Assert.Equal(2, descriptors.Count);
        Assert.Equal(typeof(ConsumerTokenService), descriptors[^1].ImplementationType);
    }

    /// <summary>Options 兜底 + IMemoryCache 兜底（AddConstructibleService 守卫工厂经 ActivatorUtilities 解析剩余参数需可解析）。</summary>
    [Fact]
    public void ConfigureServices_Registers_Options_And_Cache()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IOptions<AuthCenterOptions>>());   // AddOptions 默认值兜底
        Assert.NotNull(provider.GetRequiredService<IMemoryCache>());                  // TryAddSingleton<IMemoryCache, MemoryCache>
    }

    private sealed class ConsumerTokenService : ITokenService
    {
        public System.Threading.Tasks.Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
        public System.Threading.Tasks.Task<TokenValidationResult> ValidateTokenAsync(string accessToken, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
        public System.Threading.Tasks.Task<TokenRefreshResult> RefreshTokenAsync(string refreshToken, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
        public System.Threading.Tasks.Task RevokeTokenAsync(string jti, string reason, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
    }
}
