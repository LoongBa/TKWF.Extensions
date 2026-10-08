using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Utility.Caching;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 认证中心扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线（方案 §5.11）：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——V4.10.53（领域自治根治，ADR90）三态注册：
///     <b>13 门面</b>（接口 : IDomainService）→ <c>AddConstructibleService</c>（接口可构造守卫工厂 + 实现类
///     throw-factory，消费方经 <c>User.Use&lt;接口&gt;()</c> 解析）；<b>多 Provider</b>（<see cref="IAuthenticationProvider"/>
///     短信 + 微信 + 密码）→ <c>TryAddEnumerable</c>（多实现集合，AddConstructibleService 单实现不适用）。
///     <b>零 DataService 手动注册</b>——ADR61/D17 铁律，9 实体 DataService 经 SG1 消费方聚合自动注册；
///     Options（AuthCenterOptions）+ IMemoryCache（TokenService 黑名单前置过滤）兜底注册；</item>
/// <item>ConfigureFilters——空（认证中心非过滤器扩展；AuthorityFilter 由主框架管线处理）；</item>
/// <item><see cref="InitializeAsync"/>——签名密钥 fail-fast 预检（幂等；IServiceProvider 参数——ADR78 断代签名）。</item>
/// </list>
/// </summary>
[TKWFExtension("AuthCenter")]
    [TKWFExtensionCapability(ServiceType = typeof(ITokenService), QuerySurface = "FullIQueryable")]
    [TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.Emailing.IEmailSender), MinVersion = "0.1.1")]
    public class AuthCenterExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "AuthCenter";

    /// <summary>扩展描述。</summary>
    public override string Description => "认证中心扩展——令牌体系（RS256 JWT + Refresh rotation + 黑名单）/ 认证矩阵 / 登录保护 / 票据换令牌（PKCE）/ 身份适配层 / 跨系统映射 / 平台凭证";

    /// <summary>
    /// 注册认证中心服务。
    /// <para>V4.10.53（领域自治根治，正确路线）三态注册：</para>
    /// <list type="bullet">
    /// <item><b>门面（AddConstructibleService）</b>——14 个接口 : IDomainService（ITokenService /
    ///     IAuthLoginAttemptService / IOAuthTicketService / ISmsVerificationService / IPlatformCredentialService /
    ///     IPlatformAccountMapService / IWeChatApiClient / ITokenVerifier / IAuthAccountQueryService /
    ///     IAuthAccountService / IAuthGrantQueryService / IAuthGrantCommandService（V0.8.0 应用授权双门面）/
    ///     ISmsLoginService / IWechatLoginService）→ 接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；
    ///     消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析（AOP 路径先设 CurrentAopUser 再 GetRequiredService）。
    ///     旧形态 TryAddScoped 构造注入 <see cref="IDomainUser"/>（永不注册 DI——D01）生产解析必失败（v0.3.3 同根缺陷）。</item>
    /// <item><b>多 Provider（TryAddEnumerable）</b>——<see cref="IAuthenticationProvider"/> 短信 + 微信 + 密码：多实现集合
    ///     （按实现类型去重），AddConstructibleService 单实现不适用；实现继承 DomainServiceBase，内部 Use&lt;T&gt; 经基类 User。</item>
    /// </list>
    /// <para><see cref="ISmsSender"/> / <see cref="IAuthorizationMapper{TUserInfo}"/> 不注册默认实现——消费方实现
    /// （TryAdd 语义扩展默认优先，消费方覆盖——对齐 Emailing IEmailSender / Approval IApprovalAssigneeResolver 先例）。</para>
    /// <para>零 DataService 手动注册（ADR61/D17 铁律）——9 实体 DataService 经 SG1 消费方聚合自动注册（throw-factory），
    /// 运行期 <c>User.Use&lt;具体类&gt;()</c> NoAop 直建。</para>
    /// <para>Options：AuthCenterOptions 标 [Options("TKWF:AuthCenter")]——SG1 消费方自动绑定（模式 A 双通道）；
    /// 此处 <c>AddOptions</c> 兜底默认值（AddConstructibleService 守卫工厂经 ActivatorUtilities 解析剩余参数需
    /// IOptions 可解析；无可配置节时默认值兜底，对齐 Settings/Identity 先例）。缓存：TokenService 黑名单短 TTL
    /// 前置过滤（Oracle C3）依赖 IMemoryCache——TryAddSingleton 兜底，消费方可覆盖。</para>
    /// <para>E4 密钥管理抽象（V0.7.0）：注册 keyed <see cref="ISymmetricKeyProvider"/> 单例
    /// （<see cref="SymmetricKeyProviderKeys.AuthCenter"/> 键 → <c>FileSymmetricKeyProvider</c>——构造即加载密钥：
    /// 生产缺密钥 fail-fast / 开发随机兜底；AddKeyedSingleton 惰性构造，首次解析门面时触发，启动语义与旧
    /// PlatformCredentialKeyStore.Initialize 等价）——<see cref="PlatformCredentialService"/> 经
    /// <c>[FromKeyedServices]</c> 注入；另注册 <see cref="DevKeyCache{TKey}"/> 单例（开发模式临时 RSA 密钥集
    /// 缓存——<see cref="TokenService"/> ctor 注入，dev 分支共享密钥集，签发/验签一致性；非静态可测试并行化）。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（门面 ctor 依赖 IOptions<AuthCenterOptions>；SG1 [Options] 已在消费方自动绑定 TKWF:AuthCenter 节）
        services.AddOptions<AuthCenterOptions>();

        // E4 密钥管理抽象（V0.7.0）：keyed ISymmetricKeyProvider（FileSymmetricKeyProvider——构造即加载密钥：
        // 生产缺密钥 fail-fast / 开发随机兜底；AddKeyedSingleton 惰性构造，首次解析门面时触发，启动语义与旧
        // PlatformCredentialKeyStore.Initialize 等价）——PlatformCredentialService ctor 经 [FromKeyedServices] 注入
        services.AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(SymmetricKeyProviderKeys.AuthCenter, (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<AuthCenterOptions>>().Value;
            return new FileSymmetricKeyProvider(o.SecretEncryptionKeyPath, o.IsProduction, sp.GetService<ILogger<FileSymmetricKeyProvider>>());
        });

        // E4 密钥管理抽象（V0.7.0）：开发模式临时 RSA 密钥集缓存 DI 单例（DevKeyCache——非静态，替代 V0.5.3
        // DevRsaKeyCache 进程内静态缓存；测试可并行化）——TokenService ctor 注入，dev 分支经 _devKeys.GetOrCreate 共享
        services.AddSingleton(new DevKeyCache<TokenService.RsaKeySet>());

        // 缓存：TokenService 黑名单 IMemoryCache 短 TTL 前置过滤（Oracle C3）——TryAddSingleton 消费方可覆盖
        services.TryAddSingleton<IMemoryCache, MemoryCache>();

        // V0.9.0（ADR-密码策略与口令协议 决策 2——频控接线 T9 恢复）：点检查限流原语 fallback——
        // MemoryRateLimitCheck（框架 v4.10.67 已发布）——未启用扩展限流时密码频控"始终在"（fail-closed）；
        // 消费方显式注册 SqlCountRateLimitCheck（RateLimiting 扩展 R2）得 DB 跨实例版（TryAdd 语义业务可覆盖）。
        services.TryAddSingleton<TKW.Framework.Utility.RateLimitChecks.IRateLimitCheck, TKW.Framework.Utility.RateLimitChecks.MemoryRateLimitCheck>();

        // V4.10.53（领域自治根治）：11 门面 TryAddScoped → AddConstructibleService——接口可构造守卫工厂 + 实现类 throw-factory
        services.AddConstructibleService<ITokenService, TokenService>();
        services.AddConstructibleService<IAuthLoginAttemptService, AuthLoginAttemptService>();
        services.AddConstructibleService<IOAuthTicketService, OAuthTicketService>();
        services.AddConstructibleService<ISmsVerificationService, SmsVerificationService>();
        services.AddConstructibleService<IPlatformCredentialService, PlatformCredentialService>();
        services.AddConstructibleService<IPlatformAccountMapService, PlatformAccountMapService>();
        services.AddConstructibleService<IWeChatApiClient, WeChatApiClient>();
        services.AddConstructibleService<ITokenVerifier, LocalJwtTokenVerifier>();

        // V0.2.0：账号查询契约（对外只读查询——装配实例 / 内部复用；委托 DataService 红线合规）
        services.AddConstructibleService<IAuthAccountQueryService, AuthAccountQueryService>();

        // V0.6.0（SSO 立项，ADR-SSO Oracle P1-2）：SSO 消费面契约——实现类复用既有（AuthAccountQueryService /
        // PlatformAccountMapService 补实现 ISsoAccountQueryService / ISsoChannelMapService，返回不可变 DTO）；
        // 注册 AddConstructibleService（门面标准形态，SSO 经 User.Use<接口>() 帧内解析）
        services.AddConstructibleService<ISsoAccountQueryService, AuthAccountQueryService>();
        services.AddConstructibleService<ISsoChannelMapService, PlatformAccountMapService>();

        // V0.6.0（SSO.WeChat 评审 P1-1）：联盟锚点写契约——AuthAccountQueryService 补实现 ISsoAccountLinkService
        services.AddConstructibleService<ISsoAccountLinkService, AuthAccountQueryService>();

        // V0.2.0：账号写契约（DMP 渐进替换影子账号 upsert——ADR-Authentication-账号写契约；委托 DataService 红线合规）
        services.AddConstructibleService<IAuthAccountService, AuthAccountService>();

        // V0.8.0：应用授权双门面（Oracle P1-1——跨聚合写归门面：OAuthTicketService.ExchangeAsync 成功经
        // User.Use<IAuthGrantCommandService>() 落登录授权，不直触 AuthGrantEntityDataService）——
        // 查询只读（/grants 端点消费面）+ 写入门面（upsert 幂等，唯一约束 UX_AuthGrant_User_App_Source 兜底 TOCTOU）
        services.AddConstructibleService<IAuthGrantQueryService, AuthGrantQueryService>();
        services.AddConstructibleService<IAuthGrantCommandService, AuthGrantCommandService>();

        // 多 Provider：V4.10.55 (ADR92) 改 TryAddEnumerableConstructible——集合版守卫工厂：
        // 集合元素以守卫工厂形态注册，帧内（登录门面 User.Use<ISmsLoginService>() 等调用链）经 CurrentAopUser
        // 供给 IDomainUser 构造；帧外枚举（如控制器 [FromServices] IEnumerable<IAuthenticationProvider>）抛守卫
        // （禁止形态——表现层必须经登录编排门面消费，见 tkwf-use-extension §4.6 集合行）
        // V0.9.0（B.9）：追加 PasswordAuthenticationProvider——密码登录扩展点实现（EnabledAuthTypes fail-closed 过滤 P2-6）
        services.TryAddEnumerableConstructible<IAuthenticationProvider, SmsAuthenticationProvider>();
        services.TryAddEnumerableConstructible<IAuthenticationProvider, WeChatAuthenticationProvider>();
        services.TryAddEnumerableConstructible<IAuthenticationProvider, PasswordAuthenticationProvider>();

        // V4.10.55 (ADR92/T3 闭环)：登录编排门面——表现层零编排终态（EduPlatform 3 端点修复配套）。
        // 控制器改 User.Use<ISmsLoginService>().LoginAsync(...) / User.Use<IWechatLoginService>().LoginAsync(...)，
        // 门面内帧内枚举 IAuthenticationProvider 集合（ctor 注入 IEnumerable——守卫工厂经 CurrentAopUser 供给）。
        services.AddConstructibleService<ISmsLoginService, SmsLoginService>();
        services.AddConstructibleService<IWechatLoginService, WechatLoginService>();

        // V0.9.0（B.9）：密码登录编排门面——镜像 SmsLoginService（Identifier+Password → Password Provider → 签发）
        services.AddConstructibleService<IPasswordLoginService, PasswordLoginService>();

        // V0.9.0（B.10/B.11）：找回密码多通道门面——SMS（SmsScenes.Reset 现成）/ Email（IEmailSender 可空降级）/
        // 扫码（OAuthTicket 前置，编排归装配层）；UId-keyed 不实现 IAccountPasswordManager 第二实现
        services.AddConstructibleService<IPasswordResetService, PasswordResetService>();
    }

    /// <summary>
    /// 幂等初始化——签名密钥 fail-fast 预检（生产缺 Issuer/密钥 → InvalidOperationException 拒绝启动；开发自动生成临时密钥 + Warning）。
    /// <para>V4.10.25 (ADR78)：sp 经参数传入（替代 IServiceProviderAware setter 注入）。</para>
    /// <para>V0.3.1 修复（A' 裁定）：<c>ITokenService</c> 构造注入 3 个 *EntityDataService（经
    /// <c>AddConstructibleDataService</c> 可构造工厂注册，解析需 <c>DomainUserContext.CurrentAopUser</c> 域作用域）——
    /// 启动期 <c>InitializeExtensionsAsync(sp)</c> 无 <c>Use&lt;T&gt;()</c> 上下文，裸 <c>sp.GetService</c> 会抛守卫异常
    /// （ADR61 Oracle B4）。改为经 <see cref="BeginSystemScopeAsync"/> 进入系统作用域 + <c>scope.System.Use&lt;ITokenService&gt;()</c>
    /// 解析——<c>Use&lt;T&gt;()</c> 内设 <c>CurrentAopUser=SystemUser</c>（领域自治铁律零妥协，Oracle 方案 A' 裁决；
    /// <c>ITokenService : IDomainService</c> 空标记准入）。fail-fast 语义不变（同一 <c>LoadKeysCore</c> 单一真相源）。</para>
    /// <para><b>V0.5.4 修复（Development 模式启动崩溃——框架组转达 2026-10-05）</b>：
    /// <c>BeginSystemScopeAsync(externalSp)</c> 传参 <b>不建子 scope</b>——按 M1 所有权契约（DomainHost.cs）原样绑定
    /// 传入 provider，而 <c>InitializeAsync(IServiceProvider sp)</c> 收到的是 <b>根容器</b>（DomainHostInitializerBase
    /// ServiceProviderBuiltCallbackAsync 保持根容器）；<c>System.Use&lt;ITokenService&gt;()</c> 于是从根解析
    /// <c>AddScoped</c> 的守卫工厂 → <c>ValidateScopes=true</c>（Development）必崩
    /// （"%SERVICE%' ... from root provider"）。<b>修复：不传参</b> <c>host.BeginSystemScopeAsync()</c>——框架内部
    /// <c>_ServiceProvider.CreateScope()</c> 建子 scope 并自拥生命周期（DomainHost.cs L405-409，SystemActorApiTests
    /// "必须使用独立 IServiceScope 而非根容器"），Scoped 解析合法。同 defect 修复 Federation/Tagging/Permissions/Identity
    /// 四处（方案 A' 复制链）。</para>
    /// </summary>
    public override async Task InitializeAsync(IServiceProvider sp)
    {
        var host = sp.GetRequiredService<DomainHost<TUserInfo>>();
        await using var sysScope = await host.BeginSystemScopeAsync(); // V0.5.4：不传 sp——框架内部建子 scope（传 root 不建 scope，ValidateScopes 下 Scoped 解析崩）

        var tokenService = sysScope.System.Use<ITokenService>();
        if (tokenService is TokenService concrete)
            concrete.EnsureKeysLoaded(); // 触发 _keys 懒加载（生产 fail-fast）
    }
}
