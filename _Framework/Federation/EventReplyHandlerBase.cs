using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 事件回复 handler 基类（外部身份联邦 E3 高内聚设施——方案 docs/Federation/EventReplyHandlerBase与内置测试Handler-开发方案.md）。
/// <para>模板方法封装"签 accesscode → 构造图文（news）链接"——子类只需实现
/// <see cref="BuildReplyAsync"/>（业务规则：检查 context → 返回载荷或 null）。</para>
/// <para>高内聚核心：经 <see cref="IAccessCodeService"/>（<c>ISealedTicketService&lt;FederatedIdentity&gt;</c>
/// 的 IssueAsync 重载承载——同一实现）签 accesscode（载荷 JSON 加密落库 + 原子 CAS 核销），
/// 自动构造 <see cref="EventReplyNewsArticle"/> 图文链接（URL 指内置测试端点）——减少业务 handler 样板代码。</para>
/// <para>注册：消费方 <c>services.TryAddEnumerableConstructible&lt;IEventReplyHandler, X&gt;()</c>
/// （ADR92 集合版守卫工厂——帧内供给 IDomainUser；守卫工厂 ActivatorUtilities 构造，子类 ctor 仅 IDomainUser，
/// 配置经 <see cref="IOptions{TOptions}"/> 惰性读取——不构造注入 Options）。</para>
/// </summary>
public abstract class EventReplyHandlerBase : DomainServiceBase, IEventReplyHandler
{
    /// <summary>匹配的通道类型（wechat_event 等——编排按 ChannelType 过滤）。</summary>
    public abstract string ChannelType { get; }

    private readonly IOptions<FederationEventReplyOptions>? _eventReplyOptions;

    /// <summary>测试端点 URL 基址（消费方部署域名——图文链接拼接用；空 = 测试 handler 不回复图文）。</summary>
    protected string? TestAccessCodeEndpointBaseUrl => _eventReplyOptions?.Value.TestAccessCodeEndpointBaseUrl;

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
    /// 模板方法：子类业务载荷 → 签 accesscode（高内聚）→ 图文链接回复（<see cref="IEventReplyHandler.HandleAsync"/>）。
    /// <para>返回 null = 不处理（first-non-null-wins 语义——编排取首个非 null）。</para>
    /// </summary>
    public async Task<EventReplyResult?> HandleAsync(SsoChannelEventContext context, CancellationToken ct = default)
    {
        var payload = await BuildReplyAsync(context, ct);
        if (payload is null)
            return null;

        // 高内聚核心：签 accesscode（载荷 JSON 加密落库 + expectedClaimant 原子 CAS）→ 图文链接 URL
        var issue = await User.Use<IAccessCodeService>()
            .IssueAsync(payload.PayloadJson, payload.Ttl, payload.ExpectedClaimant, ct);
        var url = BuildTestAccessCodeUrl(issue.Code);
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
    /// <para>建议：测试/业务场景需检查部署基址（<see cref="TestAccessCodeEndpointBaseUrl"/>）非空才返回载荷
    /// （空 = 无法拼图文链接，返回 null 不拦截其它 handler——方案 P1-2/P2-1 修复）。</para>
    /// </summary>
    protected abstract Task<EventReplyPayload?> BuildReplyAsync(SsoChannelEventContext context, CancellationToken ct);

    /// <summary>构造测试端点 URL（{BaseUrl}/{RoutePrefix}/test/accesscode/{code}——与 FederationWebExtension 端点路径一致）。</summary>
    protected string BuildTestAccessCodeUrl(string code)
        => $"{TestAccessCodeEndpointBaseUrl?.TrimEnd('/')}/{RoutePrefix.Trim('/')}/test/accesscode/{code}";

    /// <summary>
    /// 子类载荷 DTO（Title/Description/PayloadJson/Ttl/ExpectedClaimant/PicUrl——签 accesscode 与图文构造输入）。
    /// </summary>
    protected sealed record EventReplyPayload(
        string Title,
        string Description,
        string PayloadJson,
        TimeSpan Ttl,
        string? ExpectedClaimant = null,
        string? PicUrl = null);
}
