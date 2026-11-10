using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;
using TKW.Framework.Domain.Ticket;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.Federation;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部 IdP 业务兑票门面（V0.14.0——DMP ADR05 方案 B 冻结契约端点 <c>POST {prefix}/login/ticket/redeem</c>）。
/// <para>流程（开发方案 §2.3）：<see cref="ISealedTicketService{T}"/>（<c>ISealedTicketService&lt;FederatedTicketPayload&gt;</c>）
/// <c>RedeemAsync(ticket, context: null)</c> 验票（P1-3 统一判因：失败/过期/已用/篡改/claimant 不符恒 null →
/// <c>TICKET_INVALID</c>；P1-2：仅消费 null-claimant 票）→ L2 账号状态检查（冻结/禁用 → 400）→
/// <b>返回 <see cref="FederatedTicketPayload"/> 明文 JSON（11 字段 + extensions）——不建会话、不发 token、
/// 不触 <see cref="IFederatedLoginOrchestrator"/></b>（会员会话由消费方自建——DMP Merchant register/免登链路）。</para>
/// <para><b>频控强制（P0-2 独立桶）</b>：<see cref="IRateLimitCheck.TryAcquire"/> 点检查（key <c>ext:ticket-redeem:{ip}</c>——
/// 与登录桶 <c>ext:ticket:{ip}</c> 不互挤；阈值归领域 <see cref="LoginProtectionOptions.TicketRedeemServicePerMinutePerIp"/>
/// 默认 60——消费方为服务间调用 IP 集中，配额语义「每后端实例」非「每用户」，防重试风暴 + DoS 纵深）。
/// 命中抛 <see cref="TKW.Framework.Domain.Exceptions.RateLimitedException"/>（429 <c>TICKET_RATE_LIMITED</c>）。</para>
/// <para><b>fail-hard</b>：<c>ISealedTicketService</c>/L2 检查依赖未装配 → 守卫异常 →
/// <see cref="TKW.Framework.Domain.Exceptions.ServiceNotConfiguredException"/>（503 <c>TICKET_SERVICE_NOT_CONFIGURED</c>）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IExternalTicketRedeemService, ExternalTicketRedeemService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
public interface IExternalTicketRedeemService : IDomainService
{
    /// <summary>业务兑票——验票（P1-3 统一 <c>TICKET_INVALID</c>）→ L2 账号状态检查（冻结/禁用 400）→
    /// 返回 <see cref="FederatedTicketPayload"/> 明文（11 字段 + extensions）；<b>不建会话、不发 token</b>。
    /// <paramref name="request"/>：<c>{ticket}</c>（body 绑定单键）；频控 IP 维度在门面内经
    /// <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/> 取服务端 <c>RemoteIpAddress</c>
    /// （不为客户端可伪造输入——B3b 生成端点无法注入 Web 层参数，IP 取读下沉门面）。</summary>
    [AllowAnonymousFlag]
    [ApiExpose(RestRoute = "/login/ticket/redeem", RestMethod = "POST", AllowAnonymous = true)]
    Task<FederatedTicketPayload> RedeemAsync(ExternalTicketRedeemRequest request, CancellationToken ct = default);
}