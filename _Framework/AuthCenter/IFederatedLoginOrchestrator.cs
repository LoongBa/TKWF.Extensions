using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 联邦登录共享编排门面（V0.10.0 P2-2 全局最优——抽取 code 通道与票据通道共享的"identity → 账号 → 签发"子编排）。
/// <para>消费方（两门户各自负责"取 FederatedIdentity"后委托本门面）：</para>
/// <list type="bullet">
/// <item><see cref="ExternalIdpLoginService"/>（外部 IdP code 通道）——桥接认证成功后构建
/// <see cref="FederatedIdentity"/>（UId 恒空，ChannelId/ExternalUserId 来自认证结果）委托本门面；</item>
/// <item><see cref="ExternalTicketLoginService"/>（票据 SPA 兑现）——<c>ISealedTicketService&lt;FederatedIdentity&gt;</c>
/// 验票成功后把载荷 identity 委托本门面（identity.UId 由事件链签发载荷决定——可能非空或为空）。</item>
/// </list>
/// <para>编排（P1-1 fallback——<see cref="FederatedIdentity.UId"/> 不保证非空，事件链子类签发先例
/// <c>SsoSubAppBridge</c> 签 <c>string.Empty</c>）：UId 非空 → <see cref="IAuthAccountQueryService.GetByUIdAsync"/>
/// 直读复用（免二次映射）；未命中或 UId 空 → <see cref="IExternalIdentityService.GetByExternalIdAsync"/>
/// (ChannelId, ExternalUserId) 映射复用；无 → 建号（V0.11.0 T6：<c>FederatedProvisioning.AutoProvision</c> 门控，
/// 停写 Phone/AuthLevel）+
/// <see cref="IExternalIdentityService.LinkAsync"/>（因素唯一约束冲突 catch 回滚重查幂等）→ 回读账号
/// （IsEnabled/IsFrozen 守卫）→ <see cref="ITokenService.IssueTokenAsync"/>（authType=<c>federated</c> +
/// <c>channel_type</c>=identity.ChannelType——null 不写 claim，P2-3）。</para>
/// <para>fail-hard：<see cref="IExternalIdentityService"/>/<see cref="IAuthAccountService"/>/<see cref="ITokenService"/>
/// 均为 AddConstructibleService 守卫工厂门面——<c>User.Use&lt;T&gt;()</c> 帧内懒加载（DI004 铁律），未装配抛守卫。</para>
/// </summary>
public interface IFederatedLoginOrchestrator : IDomainService
{
    /// <summary>联邦身份统一登录编排——identity → 账号回读/建号 → 签 token1 → <see cref="TokenResponse"/>。
    /// <para>匿名面声明：联邦登录为无会话入口（对齐 <see cref="ITokenService.IssueTokenAsync"/> E1 语义）。
    /// 失败抛 <see cref="TKW.Framework.Domain.Exceptions.DomainException"/>（400——ACCOUNT_FROZEN/ACCOUNT_DISABLED/
    /// ACCOUNT_NOT_FOUND/ACCOUNT_NOT_PROVISIONED/ACCOUNT_CREATE_FAILED 等）。</para>
    /// <para><c>ExposeRest=false</c>：共享子编排为内部服务（code 通道/票据通道经 <c>User.Use</c> 委托），不生成端点。</para></summary>
    [AllowAnonymousFlag]
    [ApiExpose(ExposeRest = false)]
    Task<TokenResponse> LoginAsync(FederatedIdentity identity, CancellationToken ct = default);
}