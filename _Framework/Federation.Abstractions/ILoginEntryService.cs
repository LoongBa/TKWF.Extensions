using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 免登入口门面（I2-8 微信免登闭环 {ticket} 缺口——方案 docs/Federation/I2-渠道关注关系域-开发方案.md §3.8）。
/// <para>建立在框架既有签票原语 <c>ISealedTicketService&lt;FederatedIdentity&gt;</c> 之上
/// （主框架 TKW.Framework.Domain.Ticket——与主包事件回复签票基类同一个签票原语，不引入第二套票据实现，R4）。
/// 两条免登能力均 fail-closed（未启用/未配置/失败 → null + Warning——不阻断事件回复）。</para>
/// <para>注册：Federation 主包 <c>AddConstructibleService&lt;ILoginEntryService, LoginEntryService&gt;()</c>；
/// 消费方经 <c>User.Use&lt;ILoginEntryService&gt;()</c> 帧内解析（DI004——契约在 Abstractions、实现在主包）。</para>
/// </summary>
public interface ILoginEntryService : IDomainService
{
    /// <summary>
    /// 签发票据并组装免登链接（URL 形态 <c>{baseUrl}{RoutePrefix}{TicketPath}?ticket={ticket}</c>——基址复用
    /// <c>FederationEventReplyOptions.TestTicketEndpointBaseUrl</c> 单一事实源，<c>FederationLoginEntryOptions.ChannelBaseUrls</c>
    /// 按 ChannelId 覆盖）。
    /// <para>fail-closed（三层）：① <see cref="FederationLoginEntryOptions.Enabled"/> 默认 false → null；
    /// ② 基址（ChannelBaseUrls 与 TestTicketEndpointBaseUrl 均空）→ null（不错拼相对链接）；
    /// ③ <c>IssueAsync</c> 抛异常（票据后端不可用）→ null（不阻断回复）。</para>
    /// </summary>
    /// <param name="channelId">渠道实例 id（如公众号 id——基址覆盖键 + 签入 identity.ChannelId）。</param>
    /// <param name="channelType">通道类型（如 <c>wechat_event</c>——签入 identity.ChannelType）。</param>
    /// <param name="externalUserId">外部用户标识（openid——签入 identity.ExternalUserId）。</param>
    /// <param name="ttlSeconds">票据 TTL 秒（null = <see cref="FederationLoginEntryOptions.TicketTtlSeconds"/>）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<string?> BuildTicketUrlAsync(
        string channelId, string channelType, string externalUserId,
        int? ttlSeconds = null, CancellationToken ct = default);

    /// <summary>
    /// 静默授权入口（snsapi_base）——委托通道 <c>ISsoChannel.BuildAuthorizeUrlAsync(Scope: "snsapi_base")</c>
    /// （无需新契约，方案 §6.1 现状即支持）。
    /// <para>返回 null = 通道构造失败（未装配平台库）/ 通道不支持授权（<c>BuildAuthorizeUrlAsync</c> DIM null）——
    /// fail-closed 降级（不抛不阻断）。</para>
    /// </summary>
    /// <param name="channelId">渠道实例 id（ISsoChannelFactory 选区键）。</param>
    /// <param name="returnUrl">授权回调地址（平台 Federation 回调端点）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<string?> BuildSilentAuthorizeUrlAsync(string channelId, string returnUrl, CancellationToken ct = default);
}

/// <summary>
/// 免登入口领域配置（<c>TKWF:Federation:LoginEntry</c> 节——配置分层 AGENTS §8：领域行为开关归领域节）。
/// <para><b>不新增 BaseUrl 配置项</b>（基址复用 <c>FederationEventReplyOptions.TestTicketEndpointBaseUrl</c> 单一事实源——
/// <see cref="ChannelBaseUrls"/> 仅按 ChannelId 覆盖，方案 §3.8/§6.3）。</para>
/// </summary>
public sealed class FederationLoginEntryOptions
{
    /// <summary>免登入口开关（默认 <c>false</c>——显式启用，fail-closed）。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>SPA 落地页路由（默认 <c>/login/ticket</c>——前端路由，非 AuthCenter API 端点）。</summary>
    public string TicketPath { get; set; } = "/login/ticket";

    /// <summary>票据 TTL 秒（默认 300——必须 ≤ AuthCenter 密封票据 TTL，方案 §6.4）。</summary>
    public int TicketTtlSeconds { get; set; } = 300;

    /// <summary>
    /// 票据 TTL 安全上限（默认 300 秒——启动期校验 <see cref="TicketTtlSeconds"/> ≤ 本值，超限 fail-fast）。
    /// <para>方案 §6.4「TicketTtlSeconds ≤ AuthCenter 密封票据 TTL，启动期校验」的本地声明：Federation 只引
    /// AuthCenter.Abstractions（无 TrustCenter Options 可见性），跨包配置无法自动比对——消费方须令本值与
    /// <c>TKWF:TrustCenter:SealedTicketExpirationSeconds</c>（默认 120）对齐或更小。</para>
    /// </summary>
    public int MaxTicketTtlSeconds { get; set; } = 300;

    /// <summary>按 ChannelId 覆盖基址（优先于 FederationEventReplyOptions.TestTicketEndpointBaseUrl）。</summary>
    public Dictionary<string, string> ChannelBaseUrls { get; set; } = new();
}