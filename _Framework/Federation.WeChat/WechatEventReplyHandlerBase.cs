using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信事件回复 handler 平台基类（方案 docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md §3.2）。
/// <para>⚠️ <b>不继承</b> <see cref="EventReplyHandlerBase"/>（Oracle P1-1——两个基类<b>并列</b>：
/// 主包签票基类 vs 平台二级分发基类，职责分离）。</para>
/// <para><b>HandleAsync 模板方法</b>：① <see cref="WechatEventContext.Parse"/> 解析
/// （Parameters 基础字段 + <c>__decrypted_xml</c> 未富化字段——Oracle P1-4）
/// → ② <see cref="DispatchAsync"/> 二级分发（MsgType/Event → 强类型虚方法，未覆盖分支 → <see cref="OnDefaultAsync"/>）
/// → ③ <b>协议门控</b>（<see cref="NoReplyEvents"/>：unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH
/// 强制返回 null——虚方法已执行可做副作用（如记录取关），回复被吞，<see cref="LogProbeBranch"/> 日志输出）
/// → ④ 业务载荷非 null → <see cref="BuildReplyResult"/> 包微信 XML 信封（text / news 图文）。</para>
/// <para><b>二级分发虚方法集</b>（方案 §3.2 分支全集——Senparc 范式 + 微信官方普通消息页）：事件类
/// OnSubscribe/OnUnsubscribe/OnScan/OnLocationEvent/OnClick/OnView/OnViewMiniProgram/OnMassSendJobFinish +
/// 消息类 OnText/OnImage/OnVoice/OnVideo/OnShortVideo/OnLocation/OnLink + 兜底 OnDefault（默认 null 不回复）。
/// 默认实现全部委托 <see cref="OnDefaultAsync"/>（Oracle P0-2 统一语义：精确分支未覆盖 → 默认 null 不回复不拦截）。</para>
/// <para><b>图文工具方法</b>（protected——模板/探针/业务 handler 共用）：<see cref="BuildTextReply"/>（信封自
/// <see cref="WeChatEventChannel"/> 上移 + 携带 ctx）/ <see cref="BuildNewsReply"/>（多图文
/// <c>&lt;ArticleCount&gt;{n}&lt;/ArticleCount&gt;</c> + <c>&lt;Articles&gt;</c> 遍历，自
/// <see cref="WeChatEventChannel"/> 上移）/ <see cref="BuildReplyResult"/>（News 优先 text 回退——包
/// <see cref="EventReplyResult"/>(Content: xml, "text/xml")）/ <see cref="ReadNewsTemplate"/>
/// （<c>Channel.Extra["news_reply"]</c> JSON 模板——<b>缺失/损坏 → null</b>，Oracle P0-2）/
/// <see cref="ReplacePlaceholders"/>（占位符替换 <c>{uid}/{openid}/{channel_id}/{channel_type}</c>；
/// <c>{ticket}</c> 不替换——本基类无签票耦合，方案 §3.3）。</para>
/// <para><b>签票能力边界</b>（方案 §3.2）：签票（<see cref="ISealedTicketService{T}"/> 图文链接）不再经继承
/// 获得——业务 handler 需要签票时经 <c>User.Use&lt;ISealedTicketService&lt;FederatedIdentity&gt;&gt;()</c>
/// 懒加载直接调（DI004 合规），或选用主包 <see cref="EventReplyHandlerBase"/>（签票专用基类）。</para>
/// <para>注册：消费方 <c>services.TryAddEnumerableConstructible&lt;IEventReplyHandler, X&gt;()</c>
/// （ADR92 集合版守卫工厂——帧内供给 IDomainUser；ctor 仅 IDomainUser + 可空 ILogger）。</para>
/// </summary>
[DiContractIgnore]
public abstract class WechatEventReplyHandlerBase : DomainServiceBase, IEventReplyHandler
{
    /// <summary>图文模板配置键（<c>Channel.Extra["news_reply"]</c>——通道级图文配置，模板驱动；Oracle P2-2
    /// 承接 TestEventReplyHandler 删除后的常量迁移至平台层）。</summary>
    public const string NewsReplyTemplateKey = "news_reply";

    /// <summary>禁回复事件（微信平台规则——回复不送达/禁回复）；TEMPLATESENDJOBFINISH 为 Oracle P2-4
    /// 补充核查项；OrdinalIgnoreCase（Oracle P2-1 采纳——Event 大小写不敏感匹配）。</summary>
    private static readonly HashSet<string> NoReplyEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "unsubscribe", "MASSSENDJOBFINISH", "TEMPLATESENDJOBFINISH",
    };

    private readonly ILogger? _logger;

    /// <summary>构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser——DomainServiceBase 基类 User；
    /// logger 为可空——守卫工厂解析不到非泛型 ILogger 时容错 NullLogger，不抛）。</summary>
    protected WechatEventReplyHandlerBase(IDomainUser user, ILogger? logger = null) : base(user)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>匹配的通道类型（编排按 ChannelType 过滤——<c>wechat_event</c> 微信事件推送）。</summary>
    public virtual string ChannelType => "wechat_event";

    /// <inheritdoc />
    public async Task<EventReplyResult?> HandleAsync(SsoChannelEventContext context, CancellationToken ct = default)
    {
        if (context?.Parameters is not { } parameters)
            return null;

        // ① 解析：Parameters（msg_type/event/...）+ __decrypted_xml（EventKey/经纬度/PicUrl/MediaId 等未富化字段——
        //   Oracle P1-4；__decrypted_xml 缺失容错不抛）
        var wechat = WechatEventContext.Parse(parameters);

        // 通道配置反查（channel_id → ChannelConfig——ReadNewsTemplate/Channel.Extra 数据源，入口一次查询；
        //   查不到 → Channel=null，模板读取自然 null——不拦截业务 handler，对齐"无法构造有效回复则 null"语义）
        wechat = wechat with { Channel = await ResolveChannelAsync(wechat.ChannelId, ct) };

        // ② 二级分发（MsgType/Event → 强类型虚方法；未覆盖分支 → OnDefaultAsync）
        var payload = await DispatchAsync(wechat, ct);

        // ③ 协议门控：NoReplyEvents（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH）强制 null
        //   ——虚方法已执行（业务副作用可做，如记录取关），回复被吞（Oracle P2-3 对齐）
        if (IsNoReplyEvent(wechat))
        {
            LogProbeBranch(wechat);   // ILogger "PROBE branch=..."（决策 A——日志区分门控分支）
            return null;
        }

        // ④ 业务载荷 → 微信 XML 信封（text / news 图文；空载荷 → null 不占 first-non-null-wins 槽）
        return payload is null ? null : BuildReplyResult(wechat, payload);
    }

    /// <summary>二级分发（MsgType/Event → 对应虚方法；未覆盖/未知类型 → <see cref="OnDefaultAsync"/>）。
    /// 匹配 OrdinalIgnoreCase（微信 Event 大小写混合——subscribe/unsubscribe 小写、SCAN/CLICK/VIEW 大写）。</summary>
    private async Task<WechatReplyPayload?> DispatchAsync(WechatEventContext ctx, CancellationToken ct)
    {
        var msgType = ctx.MsgType;
        if (string.IsNullOrEmpty(msgType))
            return await OnDefaultAsync(ctx, ct);

        if (string.Equals(msgType, "event", StringComparison.OrdinalIgnoreCase))
        {
            var e = ctx.Event;
            if (string.Equals(e, "subscribe", StringComparison.OrdinalIgnoreCase)) return await OnSubscribeAsync(ctx, ct);
            if (string.Equals(e, "unsubscribe", StringComparison.OrdinalIgnoreCase)) return await OnUnsubscribeAsync(ctx, ct);
            if (string.Equals(e, "scan", StringComparison.OrdinalIgnoreCase)) return await OnScanAsync(ctx, ct);
            if (string.Equals(e, "location", StringComparison.OrdinalIgnoreCase)) return await OnLocationEventAsync(ctx, ct);
            if (string.Equals(e, "click", StringComparison.OrdinalIgnoreCase)) return await OnClickAsync(ctx, ct);
            if (string.Equals(e, "view", StringComparison.OrdinalIgnoreCase)) return await OnViewAsync(ctx, ct);
            if (string.Equals(e, "view_miniprogram", StringComparison.OrdinalIgnoreCase)) return await OnViewMiniProgramAsync(ctx, ct);
            if (string.Equals(e, "masssendjobfinish", StringComparison.OrdinalIgnoreCase)) return await OnMassSendJobFinishAsync(ctx, ct);
            // templatessendjobfinish 亦属禁回复事件（NoReplyEvents 门控吞掉）——二级分发走默认（无专分支虚方法）
            return await OnDefaultAsync(ctx, ct);
        }

        if (string.Equals(msgType, "text", StringComparison.OrdinalIgnoreCase)) return await OnTextAsync(ctx, ct);
        if (string.Equals(msgType, "image", StringComparison.OrdinalIgnoreCase)) return await OnImageAsync(ctx, ct);
        if (string.Equals(msgType, "voice", StringComparison.OrdinalIgnoreCase)) return await OnVoiceAsync(ctx, ct);
        if (string.Equals(msgType, "video", StringComparison.OrdinalIgnoreCase)) return await OnVideoAsync(ctx, ct);
        if (string.Equals(msgType, "shortvideo", StringComparison.OrdinalIgnoreCase)) return await OnShortVideoAsync(ctx, ct);
        if (string.Equals(msgType, "location", StringComparison.OrdinalIgnoreCase)) return await OnLocationAsync(ctx, ct);
        if (string.Equals(msgType, "link", StringComparison.OrdinalIgnoreCase)) return await OnLinkAsync(ctx, ct);

        return await OnDefaultAsync(ctx, ct);
    }

    /// <summary>协议门控判定：NoReplyEvents（unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH——
    /// OrdinalIgnoreCase）命中 → 回复被吞（IsNoReplyEvent(Event)）。</summary>
    private static bool IsNoReplyEvent(WechatEventContext ctx)
        => NoReplyEvents.Contains(ctx.Event ?? string.Empty);

    /// <summary>
    /// 探针分支日志（决策 A——日志区分门控分支：禁回复事件回复被吞时输出，探针/任 handler 无需特殊处理）。
    /// <para>方案 §3.2 ①③：门控分支虚方法已执行（副作用可做），回复被吞 + 本日志记录
    /// （<c>PROBE branch=gated ...</c>——探针开启时即探针验证门控分支的证据）。</para>
    /// </summary>
    protected void LogProbeBranch(WechatEventContext ctx)
    {
        _logger.LogInformation(
            "PROBE branch=gated swallowed：MsgType={MsgType} Event={Event} ChannelId={ChannelId} OpenId={OpenId}",
            ctx.MsgType, ctx.Event, ctx.ChannelId, ctx.OpenId);
    }

    // ─── 事件类二级分发虚方法（默认全部委托 OnDefaultAsync——Oracle P0-2 统一语义） ───

    /// <summary>关注事件（subscribe——含带参二维码场景，<c>EventKey</c> 带 <c>qrscene_</c> 前缀场景值）。</summary>
    protected virtual Task<WechatReplyPayload?> OnSubscribeAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>取消关注事件（unsubscribe——回复被协议门控吞掉，副作用可经本方法执行，如记录取关）。</summary>
    protected virtual Task<WechatReplyPayload?> OnUnsubscribeAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>扫码事件（SCAN——已关注用户扫码带参，<see cref="WechatEventContext.SceneId"/> 场景值去 <c>qrscene_</c> 前缀）。</summary>
    protected virtual Task<WechatReplyPayload?> OnScanAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>上报地理位置事件（LOCATION——<see cref="WechatEventContext.Latitude"/>/<see cref="WechatEventContext.Longitude"/>/<see cref="WechatEventContext.Precision"/>）。</summary>
    protected virtual Task<WechatReplyPayload?> OnLocationEventAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>点击菜单拉取消息事件（CLICK——<see cref="WechatEventContext.EventKey"/> 菜单 key）。</summary>
    protected virtual Task<WechatReplyPayload?> OnClickAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>点击菜单跳转链接事件（VIEW——<see cref="WechatEventContext.EventKey"/> 菜单 URL）。</summary>
    protected virtual Task<WechatReplyPayload?> OnViewAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>点击菜单跳转小程序事件（VIEW_MINIPROGRAM——<see cref="WechatEventContext.EventKey"/> 小程序参数）。</summary>
    protected virtual Task<WechatReplyPayload?> OnViewMiniProgramAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>群发结果事件（MASSSENDJOBFINISH——<see cref="WechatEventContext.Status"/>/TotalCount/FilterCount/SentCount/ErrorCount；
    /// 回复被协议门控吞掉，副作用可经本方法执行）。</summary>
    protected virtual Task<WechatReplyPayload?> OnMassSendJobFinishAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    // ─── 消息类二级分发虚方法（默认全部委托 OnDefaultAsync） ───

    /// <summary>文本消息（text——<see cref="WechatEventContext.Content"/> 用户消息原文）。</summary>
    protected virtual Task<WechatReplyPayload?> OnTextAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>图片消息（image——<see cref="WechatEventContext.MediaId"/>/<see cref="WechatEventContext.PicUrl"/>）。</summary>
    protected virtual Task<WechatReplyPayload?> OnImageAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>语音消息（voice——<see cref="WechatEventContext.MediaId"/>/<see cref="WechatEventContext.Format"/>）。</summary>
    protected virtual Task<WechatReplyPayload?> OnVoiceAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>视频消息（video——<see cref="WechatEventContext.MediaId"/>/<see cref="WechatEventContext.ThumbMediaId"/>）。</summary>
    protected virtual Task<WechatReplyPayload?> OnVideoAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>小视频消息（shortvideo——<see cref="WechatEventContext.MediaId"/>/<see cref="WechatEventContext.ThumbMediaId"/>）。</summary>
    protected virtual Task<WechatReplyPayload?> OnShortVideoAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>地理位置消息（location——<see cref="WechatEventContext.Latitude"/>/<see cref="WechatEventContext.Longitude"/>/<see cref="WechatEventContext.Scale"/>/<see cref="WechatEventContext.Label"/>）。</summary>
    protected virtual Task<WechatReplyPayload?> OnLocationAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>链接消息（link——<see cref="WechatEventContext.Title"/>/<see cref="WechatEventContext.Description"/>/<see cref="WechatEventContext.Url"/>）。</summary>
    protected virtual Task<WechatReplyPayload?> OnLinkAsync(WechatEventContext ctx, CancellationToken ct) => OnDefaultAsync(ctx, ct);

    /// <summary>兜底分支（MsgType/Event 未覆盖类型）——默认返回 null（不回复不拦截，Oracle P0-2）。
    /// 派生类覆盖（如 <see cref="WechatTemplateEventReplyHandler"/> 模板驱动/探针 unknown 分支）。</summary>
    protected virtual Task<WechatReplyPayload?> OnDefaultAsync(WechatEventContext ctx, CancellationToken ct)
        => Task.FromResult<WechatReplyPayload?>(null);

    // ─── 图文工具方法（protected——模板/探针/业务 handler 共用） ───

    /// <summary>
    /// 文本回复信封组装（自 <see cref="WeChatEventChannel"/> 上移 + 参数携带 ctx）——
    /// <c>&lt;xml&gt;&lt;ToUserName&gt;&lt;![CDATA[{ToUserName}]]&gt;...&lt;MsgType&gt;text&lt;/MsgType&gt;
    /// &lt;Content&gt;&lt;![CDATA[...]]&gt;&lt;/Content&gt;&lt;/xml&gt;</c>。
    /// <para><c>ToUserName</c>=<see cref="WechatEventContext.FromUserName"/>（原消息发送者 openid = 回复对象）；
    /// <c>FromUserName</c>=<see cref="WechatEventContext.ToUserName"/>（原 ToUserName = 公众号 id）。</para>
    /// </summary>
    protected static string BuildTextReply(WechatEventContext ctx, string content)
    {
        var createTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"<xml><ToUserName><![CDATA[{ctx.FromUserName}]]></ToUserName>" +
               $"<FromUserName><![CDATA[{ctx.ToUserName}]]></FromUserName>" +
               $"<CreateTime>{createTime}</CreateTime>" +
               $"<MsgType><![CDATA[text]]></MsgType>" +
               $"<Content><![CDATA[{content}]]></Content></xml>";
    }

    /// <summary>
    /// 图文（news）回复信封组装（自 <see cref="WeChatEventChannel"/> 上移——多图文：
    /// <c>&lt;ArticleCount&gt;{n}&lt;/ArticleCount&gt;</c> + <c>&lt;Articles&gt;</c> 遍历）。
    /// <para><c>ToUserName</c>=<see cref="WechatEventContext.FromUserName"/>（openid——回复对象）；
    /// <c>FromUserName</c>=<see cref="WechatEventContext.ToUserName"/>（公众号 id）。</para>
    /// </summary>
    protected static string BuildNewsReply(WechatEventContext ctx, IReadOnlyList<EventReplyNewsArticle> articles)
    {
        var createTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sb = new System.Text.StringBuilder();
        sb.Append($"<xml><ToUserName><![CDATA[{ctx.FromUserName}]]></ToUserName>");
        sb.Append($"<FromUserName><![CDATA[{ctx.ToUserName}]]></FromUserName>");
        sb.Append($"<CreateTime>{createTime}</CreateTime>");
        sb.Append("<MsgType><![CDATA[news]]></MsgType>");
        sb.Append($"<ArticleCount>{articles.Count}</ArticleCount>");
        sb.Append("<Articles>");
        foreach (var a in articles)
        {
            sb.Append("<item>");
            sb.Append($"<Title><![CDATA[{a.Title}]]></Title>");
            sb.Append($"<Description><![CDATA[{a.Description}]]></Description>");
            sb.Append($"<PicUrl><![CDATA[{a.PicUrl ?? ""}]]></PicUrl>");
            sb.Append($"<Url><![CDATA[{a.Url}]]></Url>");
            sb.Append("</item>");
        }
        sb.Append("</Articles></xml>");
        return sb.ToString();
    }

    /// <summary>
    /// 业务载荷 → <see cref="EventReplyResult"/>（微信 XML 信封——<b>News 优先 text 回退</b>）。
    /// <para>返回 null = 空载荷（Content 与 News 均无可用内容——不占 first-non-null-wins 槽，其它 handler 继续；
    /// Handler 模板 ④ 传参防护：空载荷不产生无效 200 回复短路）。</para>
    /// </summary>
    protected static EventReplyResult? BuildReplyResult(WechatEventContext ctx, WechatReplyPayload payload)
    {
        if (payload.News is { Count: > 0 })
            return new EventReplyResult(BuildNewsReply(ctx, payload.News), "text/xml");
        if (payload.Content is not null)
            return new EventReplyResult(BuildTextReply(ctx, payload.Content), "text/xml");
        return null;
    }

    /// <summary>
    /// 读取图文模板（<c>Channel.Extra["news_reply"]</c> JSON——方案 §3.2 图文工具表）。
    /// <para><b>缺失/损坏 → null</b>（Oracle P0-2 统一语义——不回退内置模板、不拦截业务 handler；
    /// <c>enabled=false</c> 由调用方判定）。模板 JSON 形如
    /// <c>{ "title": "...", "description": "...", "link": "...", "pic_url": "...", "ttl_seconds": 300, "enabled": true }</c>。</para>
    /// </summary>
    protected static NewsReplyTemplate? ReadNewsTemplate(IReadOnlyDictionary<string, string?>? extra)
    {
        if (extra is null || !extra.TryGetValue(NewsReplyTemplateKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            return JsonSerializer.Deserialize<NewsReplyTemplate>(raw);
        }
        catch (JsonException)
        {
            return null;   // 配置损坏 → null（不拦截业务 handler——Oracle P0-2）
        }
    }

    /// <summary>
    /// 占位符替换（<c>{uid}/{openid}/{channel_id}/{channel_type}</c>——方案 §3.2 图文工具表）。
    /// <para>⚠️ <c>{ticket}</c> <b>不替换</b>（保留原样）——本基类无签票耦合（方案 §3.3 无签密封票据耦合）：
    /// 含 <c>{ticket}</c> 的 link 模板需业务 handler 签票后自行替换（选用主包
    /// <see cref="EventReplyHandlerBase"/> 或 <c>User.Use&lt;ISealedTicketService&lt;FederatedIdentity&gt;&gt;()</c>）。</para>
    /// </summary>
    protected static string ReplacePlaceholders(string text, string? uid, string? openId, string? channelId)
        => text
            .Replace("{uid}", uid ?? string.Empty)
            .Replace("{openid}", openId ?? string.Empty)
            .Replace("{channel_id}", channelId ?? string.Empty)
            .Replace("{channel_type}", "wechat_event");

    /// <summary>占位符替换便捷重载（ctx 承载 uid/openid/channel_id——<c>{ticket}</c> 不替换语义同上）。</summary>
    protected static string ReplacePlaceholders(string text, WechatEventContext ctx)
        => ReplacePlaceholders(text, ctx.UId, ctx.OpenId, ctx.ChannelId);

    /// <summary>
    /// 图文模板 DTO（<c>Channel.Extra["news_reply"]</c> JSON——Title/Description/Link 占位符模板；
    /// TtlSeconds 保留字段一致性（无签票时无业务语义，忽略）。
    /// <para>⚠️ <see cref="JsonPropertyName"/> 显式映射对齐方案文档化 snake_case 键
    /// （<c>title/description/link/pic_url/ttl_seconds/enabled</c>——2026-10-17 缺陷修复：
    /// STJ 默认反序列化大小写敏感 + snake_case 不匹配致模板恒绑定失败，潜伏缺陷自
    /// <c>TestEventReplyHandler.ReadTemplate</c> 迁移源携入，模板功能从未被真实配置验证）。</para>
    /// </summary>
    protected sealed record NewsReplyTemplate(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("link")] string? Link,
        [property: JsonPropertyName("pic_url")] string? PicUrl,
        [property: JsonPropertyName("ttl_seconds")] int? TtlSeconds,
        [property: JsonPropertyName("enabled")] bool? Enabled);

    /// <summary>通道配置反查（channel_id → ChannelConfig——入口一次查询；查不到/缺 key → null）。</summary>
    private async Task<ChannelConfig?> ResolveChannelAsync(string? channelId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return null;
        return await User.Use<IChannelRegistry>().GetByAliasOrIdAsync(channelId, ct);
    }
}