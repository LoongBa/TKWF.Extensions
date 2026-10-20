using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Ticket;

namespace TKWF.Ext.Federation;

/// <summary>
/// 事件回复 handler 基类（外部身份联邦 E3 高内聚设施——方案 docs/Federation/EventReplyHandlerBase与内置测试Handler-开发方案.md）。
/// <para>模板方法封装"签密封票据 → 构造图文（news）链接"——子类只需实现
/// <see cref="BuildReplyAsync"/>（业务规则：检查 context → 返回载荷或 null）。</para>
/// <para>高内聚核心：经 <see cref="ISealedTicketService{T}"/>（<c>ISealedTicketService&lt;FederatedIdentity&gt;</c>
/// ——TrustCenter DB 后端）签密封票据（FederatedIdentity 载荷 AES-GCM 加密落库 + ExpectedClaimant 原子 CAS），
/// 自动构造 <see cref="EventReplyNewsArticle"/> 图文链接（URL 指内置测试端点
/// <c>{BaseUrl?}/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}</c>——platformId 经
/// <see cref="IChannelRegistry"/> 从 identity.ChannelId 反查，BaseUrl 空 = 相对路径默认指向本服务同源）——
/// 减少业务 handler 样板代码。
/// （ADR109 D4 拆分命名——面① 密封票据替代旧 <c>IAccessCodeService.IssueAsync(payloadJson, ttl, claimant)</c>。）</para>
/// <para>注册：消费方 <c>services.TryAddEnumerableConstructible&lt;IEventReplyHandler, X&gt;()</c>
/// （ADR92 集合版守卫工厂——帧内供给 IDomainUser；守卫工厂 ActivatorUtilities 构造，子类 ctor 仅 IDomainUser，
/// 配置经 <see cref="IOptions{TOptions}"/> 惰性读取——不构造注入 Options）。</para>
/// </summary>
public abstract class EventReplyHandlerBase : DomainServiceBase, IEventReplyHandler
{
    /// <summary>匹配的通道类型（wechat_event 等——编排按 ChannelType 过滤）。</summary>
    public abstract string ChannelType { get; }

    private readonly IOptions<FederationEventReplyOptions>? _eventReplyOptions;

    /// <summary>测试端点 URL 基址（消费方部署域名——图文链接拼接用；空 = 相对路径 <c>/feberation/.../test/...</c> 默认指向本服务同源）。</summary>
    protected string? TestTicketEndpointBaseUrl => _eventReplyOptions?.Value.TestTicketEndpointBaseUrl;

    /// <summary>测试端点 RoutePrefix（图文链接 URL 拼接——默认 /feberation，对齐 FederationEndpointOptions.RoutePrefix）。</summary>
    protected string RoutePrefix => _eventReplyOptions?.Value.RoutePrefix ?? "/feberation";

    /// <summary>
    /// 构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser；<see cref="IOptions{TOptions}"/> 从 DI 解析——
    /// 非 IDomainService 普通注册，非构造注入守卫约束范围）。
    /// </summary>
    protected EventReplyHandlerBase(IDomainUser user, IOptions<FederationEventReplyOptions>? options = null) : base(user)
    {
        _eventReplyOptions = options;
    }

    /// <summary>
    /// 模板方法：子类业务载荷 → 签密封票据（高内聚）→ 图文链接回复（<see cref="IEventReplyHandler.HandleAsync"/>）。
    /// <para>返回 null = 不处理（first-non-null-wins 语义——编排取首个非 null）。</para>
    /// <para>数据流（W2 方案 B——机制归基类、决策归子类）：入口经 <c>dispatch.Event.ChannelId</c> 反查
    /// <see cref="ChannelConfig"/>（一次查询）→ 组装 <see cref="ReplyContext"/> 传子类 <see cref="BuildReplyAsync"/>
    /// （子类零 registry 查询，可读 <c>Channel.Extra</c> 配置）→ 签密封票据 → 图文链接拼装。
    /// （I2 语义层——handler 输入由 <see cref="SsoChannelEventContext"/> 改为 <see cref="ChannelEventDispatch"/>：
    /// channelId 从语义事件取，不再翻 Parameters 魔法键。）</para>
    /// </summary>
    public async Task<EventReplyResult?> HandleAsync(ChannelEventDispatch dispatch, CancellationToken ct = default)
    {
        // 反查通道配置（dispatch.Event.ChannelId → ChannelConfig——入口一次查询，供子类 ReplyContext + 平台段 URL 共用；
        // 查不到 → 不回复，对齐"无法构造有效图文链接则不拦截业务 handler"语义）
        var channelId = dispatch?.Event?.ChannelId;
        if (string.IsNullOrWhiteSpace(channelId))
            return null;
        var channelCfg = await User.Use<IChannelRegistry>().GetByAliasOrIdAsync(channelId, ct);
        if (channelCfg is null)
            return null;

        // 子类业务规则（收 ReplyContext——决策输入齐备）
        var payload = await BuildReplyAsync(new ReplyContext(dispatch, channelCfg), ct);
        if (payload is null)
            return null;

        // 高内聚核心：签密封票据（面① ADR109 D4——FederatedIdentity 载荷 JSON 反序列化 → AES-GCM 落库 +
        // ExpectedClaimant 原子 CAS）→ 图文链接 URL（ticket 携带 code）
        var identity = JsonSerializer.Deserialize<FederatedIdentity>(payload.PayloadJson);
        if (identity is null)
            return null;

        var issue = await User.Use<ISealedTicketService<FederatedIdentity>>()
            .IssueAsync(identity, payload.Ttl, new SealedTicketOptions(ExpectedClaimant: payload.ExpectedClaimant), ct);
        // URL 模式（模板驱动两级模型）：LinkTemplate 配置 → 占位符替换（{ticket} 等）；空 → 回退内置测试端点 URL
        var url = string.IsNullOrWhiteSpace(payload.LinkTemplate)
            ? BuildTestTicketUrl(issue.Ticket, channelCfg.PlatformType, channelCfg.ChannelId)
            : BuildTemplateUrl(payload.LinkTemplate, issue.Ticket, channelCfg, identity);
        return new EventReplyResult(
            Content: string.Empty,
            ContentType: "text/xml",
            NewsArticles:
            [
                new EventReplyNewsArticle(payload.Title, payload.Description, url, payload.PicUrl),
            ]);
    }

    /// <summary>
    /// 子类实现业务规则——返回载荷（非 null = 回复图文）或 null（不处理）。
    /// <para>基类负责入口预查通道配置 + 签票据 + 拼图文链接（含相对路径兜底）——子类无须查 registry，
    /// 直接经 <see cref="ReplyContext.Channel"/>（含 <c>Extra</c> 配置字典）做业务决策。</para>
    /// </summary>
    protected abstract Task<EventReplyPayload?> BuildReplyAsync(ReplyContext context, CancellationToken ct);

    /// <summary>事件回复决策上下文（W2 方案 B——基类备好决策输入传子类）：
    /// <see cref="Dispatch"/> 语义事件 + 已解析 UId + <see cref="Channel"/> 已解析的通道配置
    /// （含 <see cref="ChannelConfig.PlatformType"/> / <see cref="ChannelConfig.Extra"/>——模板/通道级配置读取）。
    /// <see cref="ChannelEventDispatch.Event"/> 承载语义事件（<c>ChannelId/ExternalUserId/EventType/...</c>）。</summary>
    protected sealed record ReplyContext(ChannelEventDispatch Dispatch, ChannelConfig Channel)
    {
        /// <summary>语义事件（便捷属性——ChannelId/ExternalUserId/EventType/RawPayload/Attributes 等一等字段）。</summary>
        public ChannelEvent Event => Dispatch.Event;
    }

    /// <summary>构造测试端点 URL（{BaseUrl?}/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}——与 FederationWebExtension
    /// 端点路径一致；BaseUrl 空 → 相对路径（以 / 开头，默认指向本服务同源——微信可点击）。</summary>
    protected string BuildTestTicketUrl(string ticket, string platformId, string channelId)
    {
        var relative = $"{RoutePrefix.Trim('/')}/{platformId}/test/{channelId}/{ticket}";
        var baseUrl = TestTicketEndpointBaseUrl?.TrimEnd('/');
        return string.IsNullOrEmpty(baseUrl) ? $"/{relative}" : $"{baseUrl}/{relative}";
    }

    /// <summary>构造自定义模板 URL（模板驱动两级模型——LinkTemplate 占位符替换：<c>{ticket}/{uid}/{openid}/{channel_id}/{channel_type}</c>）。</summary>
    protected string BuildTemplateUrl(string linkTemplate, string ticket, ChannelConfig channel, FederatedIdentity identity)
        => linkTemplate
            .Replace("{ticket}", ticket)
            .Replace("{uid}", identity.UId ?? string.Empty)
            .Replace("{openid}", identity.ExternalUserId ?? string.Empty)
            .Replace("{channel_id}", channel.ChannelId)
            .Replace("{channel_type}", channel.PlatformType);

    /// <summary>
    /// 子类载荷 DTO（Title/Description/PayloadJson/Ttl/ExpectedClaimant/PicUrl/LinkTemplate——签密封票据与图文构造输入；
    /// PayloadJson = <see cref="FederatedIdentity"/> 序列化 JSON——基类反序列化后经
    /// <see cref="ISealedTicketService{T}"/> 签发；LinkTemplate 空 = 内置测试端点 URL，非空 = 模板 URL
    /// （占位符 <c>{ticket}/{uid}/{openid}/{channel_id}/{channel_type}</c> 签票后替换——模板驱动两级模型第一级）。
    /// </summary>
    protected sealed record EventReplyPayload(
        string Title,
        string Description,
        string PayloadJson,
        TimeSpan Ttl,
        string? ExpectedClaimant = null,
        string? PicUrl = null,
        string? LinkTemplate = null);
}
