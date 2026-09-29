using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 认证中心扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线（方案 §5.11）：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——注册服务门面（TryAddScoped；<b>零 DataService 手动注册</b>——ADR61/D17 铁律，
/// 8 实体 DataService 经 SG1 基类类型判定 + 消费方 GetExtensionMetaContexts() 聚合 + AddConstructibleDataService 自动注册）；</item>
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
    /// 注册认证中心服务门面。
    /// <para>⚠️ <b>零 DataService 手动注册</b>（ADR61/D17 铁律）——实体型 DataService（AuthAccountEntityDataService 等 8 个）
    /// 经 SG1 基类类型判定（InheritsFromDomainDataServiceBase）→ 扩展自身 ProjectMetaContext.GetServiceRegistrations() →
    /// 消费方 DomainHost 装配时 GetExtensionMetaContexts() 聚合 + AddConstructibleDataService 可构造工厂自动注册。
    /// 实体须继承非泛型 DomainDataServiceBase&lt;TEntity,TDto&gt;（构造 IDomainUser）——ADR61 约束。</para>
    /// <para><see cref="ISmsSender"/> / <see cref="IAuthorizationMapper{TUserInfo}"/> 不注册默认实现——消费方实现
    /// （TryAdd 语义扩展默认优先，消费方覆盖——对齐 Emailing IEmailSender / Approval IApprovalAssigneeResolver 先例）。</para>
    /// <para>Options：AuthCenterOptions 标 [Options("TKWF:AuthCenter")]——SG1 消费方自动绑定（模式 A 双通道）。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.TryAddScoped<ITokenService, TokenService>();
        services.TryAddScoped<IAuthLoginAttemptService, AuthLoginAttemptService>();
        services.TryAddScoped<IOAuthTicketService, OAuthTicketService>();
        services.TryAddScoped<ISmsVerificationService, SmsVerificationService>();
        services.TryAddScoped<IPlatformCredentialService, PlatformCredentialService>();
        services.TryAddScoped<IPlatformAccountMapService, PlatformAccountMapService>();
        services.TryAddScoped<IWeChatApiClient, WeChatApiClient>();
        services.TryAddScoped<ITokenVerifier, LocalJwtTokenVerifier>();
        // 多 Provider：TryAddEnumerable（按实现类型去重——TryAddScoped 同 ServiceType 第二次会被跳过）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProvider, SmsAuthenticationProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProvider, WeChatAuthenticationProvider>());
    }

    /// <summary>
    /// 幂等初始化——签名密钥 fail-fast 预检（生产缺 Issuer/密钥 → InvalidOperationException 拒绝启动；开发自动生成临时密钥 + Warning）。
    /// <para>V4.10.25 (ADR78)：sp 经参数传入（替代 IServiceProviderAware setter 注入）。</para>
    /// </summary>
    public override Task InitializeAsync(IServiceProvider sp)
    {
        var tokenService = sp.GetService<ITokenService>();
        if (tokenService is TokenService concrete)
            concrete.EnsureKeysLoaded(); // 触发 _keys 懒加载（生产 fail-fast）
        return Task.CompletedTask;
    }
}
