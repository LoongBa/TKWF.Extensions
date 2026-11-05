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
    /// 按 ChannelId 覆盖，<paramref name="requestBaseUrl"/> 为请求 Host 推导兜底（本站点落地——反代/多域名显式配置覆盖）。
    /// <para>⚠️ <b>扩展托管 URL 遗留形态</b>：本方法组装含 RoutePrefix + TicketPath 的扩展侧 URL——<b>新链路推荐
    /// <see cref="IssueTicketAsync"/> 裸票（<c>.Ticket</c>）+ 消费方业务自拼落地 URL</b>（图文模板 <c>{ticket_value}</c> 占位符），
    /// 落地 URL 形态（业务 mid/路径）属消费方业务编排，扩展不再独占拼接（需求 B）。</para>
    /// fail-closed（三层）：① <see cref="FederationLoginEntryOptions.Enabled"/> 默认 false → null；
    /// ② 三级基址（ChannelBaseUrls / TestTicketEndpointBaseUrl / requestBaseUrl）均空 → 交 <c>CombineUrl</c>
    /// 产<b>相对路径</b>（本站点同源——微信图文 link 须完整 URL，故实际链路恒有 requestBaseUrl）；
    /// ③ <c>IssueAsync</c> 抛异常（票据后端不可用）→ null（不阻断回复）。
    /// <para>票据不落日志红线（§6.4）：本方法 dev 模式（IsProduction != true）输出完整 URL 日志（测试方受控访问）；
    /// <see cref="IssueTicketAsync"/> 裸票零出口（任何模式不落日志）。</para>
    /// </summary>
    /// <param name="channelId">渠道实例 id（如公众号 id——基址覆盖键 + 签入 identity.ChannelId）。</param>
    /// <param name="channelType">通道类型（如 <c>wechat_event</c>——签入 identity.ChannelType）。</param>
    /// <param name="externalUserId">外部用户标识（openid——签入 identity.ExternalUserId）。</param>
    /// <param name="ttlSeconds">票据 TTL 秒（null = <see cref="FederationLoginEntryOptions.TicketTtlSeconds"/>）。</param>
    /// <param name="requestBaseUrl">请求 Host 推导基址（<c>scheme://host</c>——Web 装配层注入，本站点落地；可空）。</param>
    /// <param name="uId">平台账号 uid（编排层 uid 命中时富化——签入 identity.UId，免登落地端兑换后
    /// <c>FederatedLoginOrchestrator</c> UId 直读复用；可空 = 未绑定，签入空串——编排走映射反查/自动建号兜底）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<string?> BuildTicketUrlAsync(
        string channelId, string channelType, string externalUserId,
        int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null);

    /// <summary>
    /// 签发票据并组装<b>测试端点</b> URL（URL 形态 <c>{baseUrl}{RoutePrefix}/{platformId}/test/{channelId}/{ticket}</c>——
    /// 指向 FederationWebExtension <c>HandleTestTicket</c>（GET 匿名兑票 → 200 JSON 载荷），供开发期
    /// ticket 闭环验证（内置测试图文卡片的点击落点——用户点卡片直达解票信息展示）。
    /// <para><b>与 <see cref="BuildTicketUrlAsync"/> 的差异</b>：① 路径为测试端点（<c>/test/...</c>）而非免登落地
    /// （<c>{TicketPath}?ticket=</c>）；② <b>不受 <see cref="FederationLoginEntryOptions.Enabled"/> 门控</b>——
    /// 测试端点匿名兑票（不经免登入口），故签票能力独立可用（密钥/DB 就绪即可——零配置冒烟目标）。</para>
    /// </summary>
    /// <param name="channelId">渠道实例 id（URL 路径段 + 签入 identity.ChannelId）。</param>
    /// <param name="platformId">平台段（<c>{PlatformType}</c>——微信 <c>wechat</c>；测试端点路由段）。</param>
    /// <param name="channelType">通道类型（如 <c>wechat_event</c>——签入 identity.ChannelType）。</param>
    /// <param name="externalUserId">外部用户标识（openid——签入 identity.ExternalUserId）。</param>
    /// <param name="ttlSeconds">票据 TTL 秒（null = <see cref="FederationLoginEntryOptions.TicketTtlSeconds"/>）。</param>
    /// <param name="requestBaseUrl">请求 Host 推导基址（<c>scheme://host</c>——Web 装配层注入；可空）。</param>
    /// <param name="uId">平台账号 uid（编排层 uid 命中时富化——签入 identity.UId，测试端点展示页体现已绑定用户；
    /// 可空 = 未绑定，签入空串——零配置冒烟不依赖绑定）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<string?> BuildTestTicketUrlAsync(
        string channelId, string platformId, string channelType, string externalUserId,
        int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null);

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

    /// <summary>
    /// 仅签发票据并返回<b>同票双形态</b>（裸票 + 完整免登 URL）——需求 B：落地 URL 归消费方图文模板承载，
    /// 扩展注入裸票据串（<c>{ticket_value}</c> 占位符 / 消费方自拼 URL）。
    /// <para>与 <see cref="BuildTicketUrlAsync"/> 同源签票（同一内部核心 + 同一 <see cref="FederationLoginEntryOptions"/>）：
    /// Enabled 门控 / TTL（<paramref name="ttlSeconds"/> ?? TicketTtlSeconds）/ uid 透传入载荷——行为逐项对齐。</para>
    /// <para><see cref="TicketIssueResult.Ticket"/> = 裸票据串（<c>{ticket_value}</c> 替换值 / 消费方自拼 URL 原语）；
    /// <see cref="TicketIssueResult.Url"/> = 完整免登 URL（<c>{ticket}</c> 占位符替换值——<c>{baseUrl}{RoutePrefix}{TicketPath}?ticket={Ticket}</c>，
    /// 与 <see cref="BuildTicketUrlAsync"/> 逐字节等价）。<b>两值同票同源</b>——一次签票双形态（用户裁定：双占位符并存
    /// 为合法场景，一次回复只签一次票，不置空任何一方）。</para>
    /// <para><b>裸票零日志出口</b>：本方法（含 dev 模式）不输出任何含票据的日志——完整 URL dev 日志出口
    /// 仅存于 <see cref="BuildTicketUrlAsync"/>（票据不落日志红线 §6.4 的强化形态）。</para>
    /// <para>fail-closed：未启用 / 签票失败 → null（调用方将占位符替换为空串，其余文本照常渲染）。</para>
    /// </summary>
    /// <param name="channelId">渠道实例 id（基址覆盖键 + 签入 identity.ChannelId）。</param>
    /// <param name="channelType">通道类型（如 <c>wechat_event</c>——签入 identity.ChannelType）。</param>
    /// <param name="externalUserId">外部用户标识（openid——签入 identity.ExternalUserId）。</param>
    /// <param name="ttlSeconds">票据 TTL 秒（null = <see cref="FederationLoginEntryOptions.TicketTtlSeconds"/>）。</param>
    /// <param name="requestBaseUrl">请求 Host 推导基址（<c>scheme://host</c>——Web 装配层注入；<see cref="TicketIssueResult.Url"/>
    /// 基址三级链第三级：ChannelBaseUrls → TestTicketEndpointBaseUrl → requestBaseUrl；可空）。</param>
    /// <param name="uId">平台账号 uid（编排层 uid 命中时富化——签入 identity.UId；可空 = 未绑定，签入空串）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<TicketIssueResult?> IssueTicketAsync(
        string channelId, string channelType, string externalUserId,
        int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null);
}

/// <summary>
/// 签票结果（同票双形态——需求 B 双占位符并存共用同一票值）。
/// <para><see cref="Ticket"/> = 裸票据串（<c>{ticket_value}</c> 占位符替换值 / 消费方自拼 URL 原语）；
/// <see cref="Url"/> = 完整免登 URL（<c>{ticket}</c> 占位符替换值——扩展托管 URL 遗留形态）。
/// 两值**同一张票**（一次签票双形态）；fail-closed 时整体 null。</para>
/// </summary>
public sealed record TicketIssueResult(string? Ticket, string? Url);

/// <summary>
/// 免登入口领域配置（<c>TKWF:Federation:LoginEntry</c> 节——配置分层 AGENTS §8：领域行为开关归领域节）。
/// <para><b>不新增 BaseUrl 配置项</b>（基址复用 <c>FederationEventReplyOptions.TestTicketEndpointBaseUrl</c> 单一事实源——
/// <see cref="ChannelBaseUrls"/> 仅按 ChannelId 覆盖，方案 §3.8/§6.3）。</para>
/// </summary>
public sealed class FederationLoginEntryOptions
{
    /// <summary>免登入口开关（默认 <c>false</c>——显式启用，fail-closed）。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>SPA 落地页路由（默认 <c>/login/ticket</c>——前端路由，非 AuthCenter API 端点）。
    /// <para>⚠️ <b>遗留组装形态自定义项</b>：仅 <see cref="ILoginEntryService.BuildTicketUrlAsync"/> /
    /// <see cref="TicketIssueResult.Url"/> 消费（扩展托管 URL）；裸票链路（<see cref="ILoginEntryService.IssueTicketAsync"/>
    /// 的 <see cref="TicketIssueResult.Ticket"/> / 占位符 <c>{ticket_value}</c>）不消费本项——落地 URL 由消费方图文模板承载。</para></summary>
    public string TicketPath { get; set; } = "/login/ticket";

    /// <summary>票据 TTL 秒（默认 120——与 <c>TKWF:TrustCenter:SealedTicketExpirationSeconds</c>（默认 120）对齐，
    /// 防免登票据生命周期长于密封票据生命周期；启动期校验 ≤ <see cref="MaxTicketTtlSeconds"/>）。</summary>
    public int TicketTtlSeconds { get; set; } = 120;

    /// <summary>
    /// 票据 TTL 安全上限（默认 120 秒——启动期校验 <see cref="TicketTtlSeconds"/> ≤ 本值，超限 fail-fast）。
    /// <para>免登票据 TTL 为<b>独立设置</b>：<c>ISealedTicketService&lt;FederatedIdentity&gt;.IssueAsync(identity, ttl, ...)</c>
    /// 对显式正 TTL 直接使用（无上限），<c>TKWF:TrustCenter:SealedTicketExpirationSeconds</c>（默认 120）仅是
    /// <c>ttl ≤ 0</c> 时的兜底下限——两者无强制大小关系。默认值对齐 120（P1-2——防直觉冲突：
    /// 免登票据缺省 300s 长于密封票据缺省 120s 造成"票据未到期已不可核销"的误解）；消费方可按需调大 MaxTicketTtlSeconds。</para>
    /// </summary>
    public int MaxTicketTtlSeconds { get; set; } = 120;

    /// <summary>按 ChannelId 覆盖基址（优先于 FederationEventReplyOptions.TestTicketEndpointBaseUrl）。</summary>
    public Dictionary<string, string> ChannelBaseUrls { get; set; } = new();
}