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
/// <item>22 门面（接口 : IDomainService）→ <c>AddConstructibleService</c>：接口 = 可构造守卫工厂
///     （非实现映射）+ 实现类 = throw-factory；域作用域外（无 CurrentAopUser）直接 DI 解析接口必抛领域架构守卫；</item>
/// <item><see cref="IAuthenticationProvider"/> 双实现 → V4.10.55（ADR92）<c>TryAddEnumerableConstructible</c>
///     （集合版守卫工厂——工厂委托形态 + 帧内 CurrentAopUser 供给；帧外枚举抛守卫）。</item>
/// <item><see cref="ISmsLoginService"/>/<see cref="IExternalIdpLoginService"/> 登录编排门面（V4.10.55 ADR92/T3 闭环）→
///     <c>AddConstructibleService</c>——表现层经 <c>User.Use&lt;门面&gt;()</c> 帧内编排。
///     ⚠️ T5（2026-10-09 三层边界）：微信门面（IWechatLoginService）/微信客户端（IWeChatApiClient）已删——
///     外部 IdP 登录统一经 <see cref="IExternalIdpLoginService"/> 桥接。
///     V0.10.0（P2-2 全局最优）：新增 <see cref="IFederatedLoginOrchestrator"/>（共享编排——code 通道 +
///     票据通道统一委托）+ <see cref="IExternalTicketLoginService"/>（票据 SPA 兑现）——同款 AddConstructibleService。</item>
/// </list>
/// </summary>
public class AuthCenterInitializerTests
{
    /// <summary>22 个 AddConstructibleService 门面（接口 → 实现）——覆盖 Initializer ConfigureServices 全部门面注册。
    /// V0.10.0 补齐：+IFederatedLoginOrchestrator / +IExternalTicketLoginService（P1-1 审核闭环）；
    /// 顺带补全既有缺漏：+ISsoAccountQueryService / +IAuthGrantQueryService / +IAuthGrantCommandService /
    /// +IPasswordLoginService / +IPasswordResetService。
    /// V0.11.0（T4 契约合并）：−ISsoChannelMapService / −ISsoAccountLinkService（并入 IExternalIdentityService）；
    /// 新增 +IExternalIdentityService / +IAuthFactorService / +IFreezeService / +IFreezeStateProvider。</summary>
    private static readonly (System.Type Interface, System.Type Impl)[] Facades =
    [
        (typeof(ITokenService), typeof(TokenService)),
        (typeof(IAuthLoginAttemptService), typeof(AuthLoginAttemptService)),
        (typeof(IOAuthTicketService), typeof(OAuthTicketService)),
        (typeof(ISmsVerificationService), typeof(SmsVerificationService)),
        (typeof(IPlatformCredentialService), typeof(PlatformCredentialService)),
        (typeof(IPlatformAccountMapService), typeof(PlatformAccountMapService)),
        (typeof(ITokenVerifier), typeof(LocalJwtTokenVerifier)),
        (typeof(IAuthAccountQueryService), typeof(AuthAccountQueryService)),
        (typeof(IAuthAccountService), typeof(AuthAccountService)),
        (typeof(ISmsLoginService), typeof(SmsLoginService)),
        (typeof(IExternalIdpLoginService), typeof(ExternalIdpLoginService)),
        // V0.6.0 SSO 消费面契约（T4：ISsoChannelMapService/ISsoAccountLinkService 已并入 IExternalIdentityService）
        (typeof(ISsoAccountQueryService), typeof(AuthAccountQueryService)),
        // V0.8.0 应用授权双门面
        (typeof(IAuthGrantQueryService), typeof(AuthGrantQueryService)),
        (typeof(IAuthGrantCommandService), typeof(AuthGrantCommandService)),
        // V0.9.0 密码能力
        (typeof(IPasswordLoginService), typeof(PasswordLoginService)),
        (typeof(IPasswordResetService), typeof(PasswordResetService)),
        // V0.10.0 外部 IdP 票据 SPA 兑现（P2-2 共享编排 + 票据门面）
        (typeof(IFederatedLoginOrchestrator), typeof(FederatedLoginOrchestrator)),
        (typeof(IExternalTicketLoginService), typeof(ExternalTicketLoginService)),
        // V0.11.0（Iteration 1）：因素/冻结门面 + 外部身份统一契约（T4 合并落点，实现类复用 PlatformAccountMapService）
        (typeof(IAuthFactorService), typeof(AuthFactorService)),
        (typeof(IFreezeService), typeof(FreezeService)),
        (typeof(IFreezeStateProvider), typeof(FreezeStateProvider)),
        (typeof(IExternalIdentityService), typeof(PlatformAccountMapService))
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

    /// <summary>V4.10.53：全部门面接口注册为可构造守卫工厂（ImplementationFactory，非实现映射）+ Scoped。</summary>
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

    /// <summary>V4.10.53：全部门面实现类注册为 throw-factory——禁止直接 DI 解析（必须经 User.Use&lt;接口&gt;()）。</summary>
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

    /// <summary>多 Provider——V4.10.55（ADR92）改 TryAddEnumerableConstructible（集合版守卫工厂）：实现以工厂委托形态注册（ImplementationType=null + ImplementationFactory 非空）。
    /// ⚠️ T5（2026-10-09）：微信 Provider 已删——短信 + 密码双实现（WeChatAuthenticationProvider 移除）。</summary>
    [Fact]
    public void ConfigureServices_Registers_TwoProviders_Enumerable()
    {
        var services = new ServiceCollection();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IAuthenticationProvider)).ToList();
        Assert.Equal(2, descriptors.Count);   // 短信 + 密码（V0.9.0 B.9；微信已删 T5）
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

        foreach (var implType in new[] { typeof(SmsAuthenticationProvider), typeof(PasswordAuthenticationProvider) })
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
        foreach (var facade in new[] { typeof(ISmsLoginService), typeof(IExternalIdpLoginService) })
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
        public System.Threading.Tasks.Task<TokenResponse> RefreshTokenAsync(RefreshRequest request, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
        public System.Threading.Tasks.Task RevokeTokenAsync(string jti, string reason, System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
        public System.Threading.Tasks.Task<VerifyResponse> GetCurrentTokenInfoAsync(System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
        public System.Threading.Tasks.Task RevokeCurrentSessionAsync(System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException();
    }
}
