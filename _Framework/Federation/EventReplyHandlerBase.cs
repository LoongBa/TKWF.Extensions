using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Ticket;
using TKWF.Ext.TrustCenter;

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

    /// <summary>内置测试 handler 注册开关（默认 false——P1-1 fail-safe；false 时 BuildReplyAsync 应返回 null）。</summary>
    protected bool TestEventReplyHandlerEnabled => _eventReplyOptions?.Value.TestEventReplyHandlerEnabled ?? false;

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
    /// </summary>
    public async Task<EventReplyResult?> HandleAsync(SsoChannelEventContext context, CancellationToken ct = default)
    {
        var payload = await BuildReplyAsync(context, ct);
        if (payload is null)
            return null;

        // 高内聚核心：签密封票据（面① ADR109 D4——FederatedIdentity 载荷 JSON 反序列化 → AES-GCM 落库 +
        // ExpectedClaimant 原子 CAS）→ 图文链接 URL（ticket 携带 code）
        var identity = JsonSerializer.Deserialize<FederatedIdentity>(payload.PayloadJson);
        if (identity is null)
            return null;

        // 反查平台段（channelId → PlatformType——图文链接 {platformId} 段；查不到 → 不回复，
        // 对齐"无法构造有效图文链接则不拦截业务 handler"语义）
        var channelCfg = await User.Use<IChannelRegistry>().GetByAliasOrIdAsync(identity.ChannelId, ct);
        if (channelCfg is null)
            return null;

        var issue = await User.Use<ISealedTicketService<FederatedIdentity>>()
            .IssueAsync(identity, payload.Ttl, new SealedTicketOptions(ExpectedClaimant: payload.ExpectedClaimant), ct);
        var url = BuildTestTicketUrl(issue.Ticket, channelCfg.PlatformType, identity.ChannelId);
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
    /// <para>基类负责签票据 + 反查平台段 + 拼图文链接（含相对路径兜底）——子类无需检查部署基址。</para>
    /// </summary>
    protected abstract Task<EventReplyPayload?> BuildReplyAsync(SsoChannelEventContext context, CancellationToken ct);

    /// <summary>构造测试端点 URL（{BaseUrl?}/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}——与 FederationWebExtension
    /// 端点路径一致；BaseUrl 空 → 相对路径（以 / 开头，默认指向本服务同源——微信可点击）。</summary>
    protected string BuildTestTicketUrl(string ticket, string platformId, string channelId)
    {
        var relative = $"{RoutePrefix.Trim('/')}/{platformId}/test/{channelId}/{ticket}";
        var baseUrl = TestTicketEndpointBaseUrl?.TrimEnd('/');
        return string.IsNullOrEmpty(baseUrl) ? $"/{relative}" : $"{baseUrl}/{relative}";
    }

    /// <summary>
    /// 子类载荷 DTO（Title/Description/PayloadJson/Ttl/ExpectedClaimant/PicUrl——签密封票据与图文构造输入；
    /// PayloadJson = <see cref="FederatedIdentity"/> 序列化 JSON——基类反序列化后经
    /// <see cref="ISealedTicketService{T}"/> 签发）。
    /// </summary>
    protected sealed record EventReplyPayload(
        string Title,
        string Description,
        string PayloadJson,
        TimeSpan Ttl,
        string? ExpectedClaimant = null,
        string? PicUrl = null);
}
