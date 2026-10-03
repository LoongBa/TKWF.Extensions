using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 认证中心扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线（方案 §5.11）：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——V4.10.53（领域自治根治，ADR90）三态注册：
///     <b>11 门面</b>（接口 : IDomainService）→ <c>AddConstructibleService</c>（接口可构造守卫工厂 + 实现类
///     throw-factory，消费方经 <c>User.Use&lt;接口&gt;()</c> 解析）；<b>接线型</b>
///     <see cref="IUserProfileSource"/>（UserCenter.Abstractions 契约非 IDomainService）→ <c>TryAddScoped</c>
///     普通 DI（被 UserCenterQueryService 经 GetService 解析）；<b>多 Provider</b>（<see cref="IAuthenticationProvider"/>
///     短信 + 微信）→ <c>TryAddEnumerable</c>（多实现集合，AddConstructibleService 单实现不适用）。
///     <b>零 DataService 手动注册</b>——ADR61/D17 铁律，8 实体 DataService 经 SG1 消费方聚合自动注册；
///     Options（AuthCenterOptions）+ IMemoryCache（TokenService 黑名单前置过滤）兜底注册；</item>
/// <item>ConfigureFilters——空（认证中心非过滤器扩展；AuthorityFilter 由主框架管线处理）；</item>
/// <item><see cref="InitializeAsync"/>——签名密钥 fail-fast 预检（幂等；IServiceProvider 参数——ADR78 断代签名）。</item>
/// </list>
/// </summary>
[TKWFExtension("Authentication")]
public class AuthCenterExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "Authentication";

    /// <summary>扩展描述。</summary>
    public override string Description => "认证中心扩展——令牌体系（RS256 JWT + Refresh rotation + 黑名单）/ 认证矩阵 / 登录保护 / 票据换令牌（PKCE）/ 身份适配层 / 跨系统映射 / 平台凭证";

    /// <summary>
    /// 注册认证中心服务。
    /// <para>V4.10.53（领域自治根治，正确路线）三态注册：</para>
    /// <list type="bullet">
    /// <item><b>门面（AddConstructibleService）</b>——11 个接口 : IDomainService（ITokenService /
    ///     IAuthLoginAttemptService / IOAuthTicketService / ISmsVerificationService / IPlatformCredentialService /
    ///     IPlatformAccountMapService / IWeChatApiClient / ITokenVerifier / IAuthAccountQueryService /
    ///     IAuthAccountService）→ 接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；
    ///     消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析（AOP 路径先设 CurrentAopUser 再 GetRequiredService）。
    ///     旧形态 TryAddScoped 构造注入 <see cref="IDomainUser"/>（永不注册 DI——D01）生产解析必失败（v0.3.3 同根缺陷）。</item>
    /// <item><b>接线型（TryAddScoped 普通 DI）</b>——<see cref="IUserProfileSource"/>（UserCenter.Abstractions 契约，
    ///     非 IDomainService 不可修改）：UserCenterQueryService 经 <c>GetService</c> 解析；TryAdd 语义扩展默认优先、消费方可覆盖。</item>
    /// <item><b>多 Provider（TryAddEnumerable）</b>——<see cref="IAuthenticationProvider"/> 短信 + 微信：多实现集合
    ///     （按实现类型去重），AddConstructibleService 单实现不适用；实现继承 DomainServiceBase，内部 Use&lt;T&gt; 经基类 User。</item>
    /// </list>
    /// <para><see cref="ISmsSender"/> / <see cref="IAuthorizationMapper{TUserInfo}"/> 不注册默认实现——消费方实现
    /// （TryAdd 语义扩展默认优先，消费方覆盖——对齐 Emailing IEmailSender / Approval IApprovalAssigneeResolver 先例）。</para>
    /// <para>零 DataService 手动注册（ADR61/D17 铁律）——8 实体 DataService 经 SG1 消费方聚合自动注册（throw-factory），
    /// 运行期 <c>User.Use&lt;具体类&gt;()</c> NoAop 直建。</para>
    /// <para>Options：AuthCenterOptions 标 [Options("TKWF:AuthCenter")]——SG1 消费方自动绑定（模式 A 双通道）；
    /// 此处 <c>AddOptions</c> 兜底默认值（AddConstructibleService 守卫工厂经 ActivatorUtilities 解析剩余参数需
    /// IOptions 可解析；无可配置节时默认值兜底，对齐 Settings/Identity 先例）。缓存：TokenService 黑名单短 TTL
    /// 前置过滤（Oracle C3）依赖 IMemoryCache——TryAddSingleton 兜底，消费方可覆盖。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（门面 ctor 依赖 IOptions<AuthCenterOptions>；SG1 [Options] 已在消费方自动绑定 TKWF:AuthCenter 节）
        services.AddOptions<AuthCenterOptions>();

        // 缓存：TokenService 黑名单 IMemoryCache 短 TTL 前置过滤（Oracle C3）——TryAddSingleton 消费方可覆盖
        services.TryAddSingleton<IMemoryCache, MemoryCache>();

        // V4.10.53（领域自治根治）：11 门面 TryAddScoped → AddConstructibleService——接口可构造守卫工厂 + 实现类 throw-factory
        services.AddConstructibleService<ITokenService, TokenService>();
        services.AddConstructibleService<IAuthLoginAttemptService, AuthLoginAttemptService>();
        services.AddConstructibleService<IOAuthTicketService, OAuthTicketService>();
        services.AddConstructibleService<ISmsVerificationService, SmsVerificationService>();
        services.AddConstructibleService<IPlatformCredentialService, PlatformCredentialService>();
        services.AddConstructibleService<IPlatformAccountMapService, PlatformAccountMapService>();
        services.AddConstructibleService<IWeChatApiClient, WeChatApiClient>();
        services.AddConstructibleService<ITokenVerifier, LocalJwtTokenVerifier>();

        // V0.2.0：账号查询契约（对外只读查询——UserCenter 桥接 / 装配实例 / 内部复用；委托 DataService 红线合规）
        services.AddConstructibleService<IAuthAccountQueryService, AuthAccountQueryService>();

        // V0.2.0：账号写契约（DMP 渐进替换影子账号 upsert——ADR-Authentication-账号写契约；委托 DataService 红线合规）
        services.AddConstructibleService<IAuthAccountService, AuthAccountService>();

        // V0.2.0：UserCenter 公共档案源实现——接线型（IUserProfileSource 非 IDomainService 契约不可修改，
        // 被 UserCenterQueryService 经普通 DI 解析）；TryAdd 语义消费方可覆盖（数据属主承接契约——装配实例零桥接）
        services.TryAddScoped<IUserProfileSource, AuthAccountUserProfileSource>();

        // 多 Provider：TryAddEnumerable（按实现类型去重——TryAddScoped 同 ServiceType 第二次会被跳过）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProvider, SmsAuthenticationProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProvider, WeChatAuthenticationProvider>());
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
    /// </summary>
    public override async Task InitializeAsync(IServiceProvider sp)
    {
        var host = sp.GetRequiredService<DomainHost<TUserInfo>>();
        await using var sysScope = await host.BeginSystemScopeAsync(sp);

        var tokenService = sysScope.System.Use<ITokenService>();
        if (tokenService is TokenService concrete)
            concrete.EnsureKeysLoaded(); // 触发 _keys 懒加载（生产 fail-fast）
    }
}
