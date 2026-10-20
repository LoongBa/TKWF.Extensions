using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

using TKWF.Ext.Federation;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 模板驱动 handler（方案 docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md §3.3——
/// 消费方<b>显式注册</b>，Oracle P0-1：平台库不默认注册 handler 实例防过度设计）。
/// <para>覆盖 <see cref="WechatEventReplyHandlerBase.OnDefaultAsync"/>——读
/// <c>Channel.Extra["news_reply"]</c> 模板构造图文（占位符 <c>{uid}/{openid}/{channel_id}/{channel_type}</c> 替换）；
/// 模板<b>缺失 / 损坏 / Enabled=false → 返回 null</b>（不回复不拦截——Oracle P0-2）。</para>
/// <para><b>多图文升级（V0.4.1，方案 WeChatOperations §3.2.3）</b>：<c>articles</c> 数组优先（多图文 1..8 篇，
/// 占位符替换逐篇应用），缺省回退单图文字段（title/description/link/pic_url——存量兼容零破坏）。</para>
/// <para><b>无签票耦合</b>（Oracle 方案 §3.3"无签密封票据耦合"）：本 handler 为纯图文回复场景，
/// <c>link</c> 模板的 <c>{ticket}</c> 占位符<b>不替换（保留原样）</b>——签票前不可用，
/// 业务 handler 需签票时选用主包 <see cref="EventReplyHandlerBase"/>（签票专用基类）或
/// <c>User.Use&lt;ISealedTicketService&lt;FederatedIdentity&gt;&gt;()</c> 懒加载签票后自行替换。</para>
/// <para>注册（消费方）：<c>services.TryAddEnumerableConstructible&lt;IEventReplyHandler, WechatTemplateEventReplyHandler&gt;()</c>；
/// 与 <see cref="WechatProbeEventReplyHandler"/> <b>互斥使用</b>（Oracle P2-5——同时注册时 first-non-null-wins，
/// 幂等语义，使用指南声明）。</para>
/// </summary>
[DiContractIgnore]
public sealed class WechatTemplateEventReplyHandler : WechatEventReplyHandlerBase
{
    /// <summary>
    /// 构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser——logger 可空容错 NullLogger，
    /// 对齐 <see cref="WechatEventReplyHandlerBase"/> 形态）。
    /// </summary>
    public WechatTemplateEventReplyHandler(IDomainUser user, ILogger? logger = null) : base(user, logger)
    {
    }

    /// <summary>
    /// 兜底分支（模板驱动）：读 <c>Channel.Extra["news_reply"]</c> 模板 → 构造图文回复载荷。
    /// <para><b>模板缺失/损坏 → null</b>（Oracle P0-2）；<c>Enabled ≠ true</c> → null
    /// （不回复不拦截——业务 handler 正常流转）；<c>Channel</c> 未富化（registry 查不到）→ <c>Channel.Extra</c> 为
    /// null → 模板读取 null → 不回复。</para>
    /// <para><b>多图文（V0.4.1）</b>：<see cref="ReadMultiNewsTemplate"/> <c>articles</c> 数组优先（1..8 篇，
    /// 占位符替换逐篇应用），缺省回退单图文字段（存量兼容）；<b>I2-8 {ticket} 支持</b>——模板含
    /// <c>{ticket}</c> 时经 <see cref="WechatEventReplyHandlerBase.ResolveTicketAsync"/> 签免登票据
    /// （<c>TtlSeconds</c> 模板 TTL 优先——票 URL 替换进 link/description）。</para>
    /// </summary>
    protected override async Task<WechatReplyPayload?> OnDefaultAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 模板读取（Extra["news_reply"]——通道级图文配置；缺失/损坏 → null，Oracle P0-2）
        var template = ReadMultiNewsTemplate(ctx.Channel?.Extra);
        if (template is null or { Enabled: not true })
            return null;

        // I2-8：签一次免登票据（模板 TtlSeconds 优先 → FederationLoginEntryOptions.TicketTtlSeconds 缺省——ResolveTicketAsync 内）
        var ticketUrl = await ResolveTicketAsync(ctx, ct, template.TtlSeconds);

        // 占位符替换逐篇应用（{uid}/{openid}/{channel_id}/{channel_type}/{ticket}——I2-8 5 参重载）
        var articles = BuildArticles(template, ctx, ticketUrl);

        return new WechatReplyPayload(News: articles);
    }

    /// <summary>
    /// 多图文模板读取（<c>Channel.Extra["news_reply"]</c> JSON——<see cref="NewsReplyTemplateV2"/>；
    /// <b>缺失/损坏 → null</b>，Oracle P0-2 统一语义——不回退内置模板、不拦截业务 handler；
    /// <c>enabled=false</c> 由调用方判定）。
    /// <para>⚠️ 平台基类 <see cref="WechatEventReplyHandlerBase.ReadNewsTemplate"/>（单图文
    /// <see cref="WechatEventReplyHandlerBase.NewsReplyTemplate"/>）V0.4.0 既有不动——本 handler V0.4.1
    /// 升级走自有解析（<c>articles</c> 数组优先 + 单图文兼容回退）。</para>
    /// </summary>
    private static NewsReplyTemplateV2? ReadMultiNewsTemplate(IReadOnlyDictionary<string, string?>? extra)
    {
        if (extra is null || !extra.TryGetValue(NewsReplyTemplateKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            return JsonSerializer.Deserialize<NewsReplyTemplateV2>(raw);
        }
        catch (JsonException)
        {
            return null;   // 配置损坏 → null（不拦截业务 handler——Oracle P0-2）
        }
    }

    /// <summary>
    /// 图文条目构建——<see cref="NewsReplyTemplateV2.Articles"/> 数组优先（多图文，微信上限 8 篇——
    /// <c>Take(8)</c> 钳制；空数组视同缺省回退）；缺省回退单图文字段（Title/Description/Link/PicUrl——存量兼容）。
    /// 占位符替换逐篇应用（I2-8：5 参重载含 <c>{ticket}</c>——<paramref name="ticketUrl"/> 可空 → 空串替换）。
    /// </summary>
    private static IReadOnlyList<EventReplyNewsArticle> BuildArticles(NewsReplyTemplateV2 template, WechatEventContext ctx, string? ticketUrl)
    {
        if (template.Articles is { Count: > 0 })
        {
            return template.Articles
                .Take(8)
                .Select(a => new EventReplyNewsArticle(
                    ReplacePlaceholders(a.Title ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                    ReplacePlaceholders(a.Description ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                    ReplacePlaceholders(a.Link ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                    a.PicUrl))
                .ToList();
        }

        // 单图文兼容回退（缺 articles 字段 / 空数组 → title/description/link/pic_url 单图文字段）
        return
        [
            new EventReplyNewsArticle(
                ReplacePlaceholders(template.Title ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                ReplacePlaceholders(template.Description ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                ReplacePlaceholders(template.Link ?? string.Empty, ctx.UId, ctx.OpenId, ctx.ChannelId, ticketUrl),
                template.PicUrl),
        ];
    }
}
