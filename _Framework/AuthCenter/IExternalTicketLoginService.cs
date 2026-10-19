using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;
using TKW.Framework.Domain.Ticket;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部 IdP 票据 SPA 兑现门面（V0.10.0——事件取 OpenId 链路"票 → token1"前端兑换面）。
/// <para>流程（方案 §5.2）：<see cref="ISealedTicketService{T}"/>（<c>ISealedTicketService&lt;FederatedIdentity&gt;</c>——
/// 主框架 D3 契约，事件链 <c>EventReplyHandlerBase</c> 签发）<c>RedeemAsync(ticket, context: null)</c> 验票
/// （ExpectedClaimant=null 匿名浏览器语义——SPA 无 app 身份）→ 取 <see cref="FederatedIdentity"/> 载荷 →
/// 委托 <see cref="IFederatedLoginOrchestrator"/>（P2-2 共享编排——身份回读/建号/签发）。</para>
/// <para><b>P1-3 统一判因</b>：<c>RedeemAsync</c> 失败/过期/已用/篡改/核销人不符<b>恒返回 null</b>（契约不泄露判因）——
/// 统一映射 <c>TICKET_INVALID</c>（无 <c>TICKET_EXPIRED</c>/<c>TICKET_USED</c> 细分）。</para>
/// <para><b>P1-2 约束</b>：本门面仅消费事件链签发时 <c>ExpectedClaimant=null</c> 的票（任意核销人可兑）；
/// 带 claimant 的票 <c>RedeemAsync(context: null)</c> CAS 恒败 → <c>TICKET_INVALID</c>（安全正确 fail，非隐蔽失效）。</para>
/// <para><b>P2-4 频控强制</b>：门面前置 <see cref="IRateLimitCheck.TryAcquire"/> 点检查（key <c>ext:ticket:{ip}</c>，
/// 强度阈值归领域 <see cref="LoginProtectionOptions.TicketRedeemPerMinutePerIp"/>——未启用扩展限流时
/// Initializer fallback <c>MemoryRateLimitCheck</c> 兜底"始终在"，fail-closed）。</para>
/// </summary>
public interface IExternalTicketLoginService : IDomainService
{
    /// <summary>票据 SPA 兑现登录——验票 → 取 FederatedIdentity → 委托 <see cref="IFederatedLoginOrchestrator"/> →
    /// 签 token1 → <see cref="LoginResult"/>。
    /// <paramref name="ticket"/> 必填（32B base64url 密封票据——事件链图文链接携带）；
    /// <paramref name="channelId"/> 可选（多通道消歧——identity.ChannelId 为空时兜底，正常由票载荷签入）；
    /// <paramref name="ipAddress"/> 可选（频控维度——Web 层经 <c>ctx.Connection.RemoteIpAddress</c> 传入）。</summary>
    [AllowAnonymousFlag]
    Task<LoginResult> LoginAsync(string ticket, string? channelId, string? ipAddress, CancellationToken ct = default);
}