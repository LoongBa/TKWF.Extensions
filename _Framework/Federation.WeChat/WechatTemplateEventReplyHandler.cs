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
/// <para>覆盖 <see cref="WechatEventReplyHandlerBase.OnDefaultAsync"/>——<b>模板解析链 4 级</b>：
/// ① <c>Channel.Extra["news_reply"]</c>（per-channel：静态投影 / DB 动态权威层）→
/// ② <see cref="WechatEventReplyOptions.DefaultNewsReplyTemplate"/>（全局默认）→
/// ③ <see cref="WechatEventReplyOptions.EnableBuiltInTestTemplate"/> 内置测试卡片（开发期零 seed 冒烟）→
/// ④ <c>null</c>（纯文本模式，不回复图文）。占位符 <c>{uid}/{openid}/{channel_id}/{channel_type}</c> 替换，
/// <c>{ticket}</c> 经签票替换为免登链接。</para>
/// <para><b>多图文升级（V0.4.1，方案 WeChatOperations §3.2.3）</b>：<c>articles</c> 数组优先（多图文 1..8 篇，
/// 占位符替换逐篇应用），缺省回退单图文字段（title/description/link/pic_url——存量兼容零破坏）。</para>
/// <para><b>免登链接基址解析链（BuildTicketUrlAsync 内，三级）</b>：
/// <c>FederationLoginEntryOptions.ChannelBaseUrls[channelId]</c> → <see cref="FederationEventReplyOptions.TestTicketEndpointBaseUrl"/>
/// → <c>ctx.BaseUrl</c>（装配层请求 Host 注入 <c>scheme://host</c>——本站点落地，零配置直觉）；
/// 三级均空 → <c>CombineUrl</c> 相对路径兜底（微信图文 link 实际链路恒有 <c>ctx.BaseUrl</c>）。</para>
/// <para>注册（消费方）：<c>services.TryAddEnumerableConstructible&lt;IEventReplyHandler, WechatTemplateEventReplyHandler&gt;()</c>；
/// 与 <see cref="WechatProbeEventReplyHandler"/> <b>互斥使用</b>（Oracle P2-5——同时注册时 first-non-null-wins，
/// 幂等语义，使用指南声明）。</para>
/// <para><b>v0.7.0 升级为可继承 T1 业务基座（排他单模型）</b>：解除 sealed——特殊业务需求
/// （如规则驱动 <c>WeChatOperationsReplyHandler</c>）<b>继承本类</b>而非裸基类
/// <see cref="WechatEventReplyHandlerBase"/>——未覆盖分支天然走模板兜底（OnDefaultAsync 继承），
/// 模板渲染/签票能力随继承复用，消除重复实现。</para>
/// </summary>
[DiContractIgnore]
public class WechatTemplateEventReplyHandler : WechatEventReplyHandlerBase
{
    /// <summary>
    /// 构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser——logger/options 从 DI 解析（可空
    /// 容错，对齐 <see cref="WechatEventReplyHandlerBase"/> 形态）；<see cref="WechatEventReplyOptions"/>
    /// 供 <see cref="RequireUidForTicket"/> 门控读取）。
    /// </summary>
    public WechatTemplateEventReplyHandler(
        IDomainUser user,
        Microsoft.Extensions.Options.IOptions<WechatEventReplyOptions>? options = null,
        ILogger? logger = null) : base(user, logger)
    {
        _replyOptions = options;
    }

    private readonly Microsoft.Extensions.Options.IOptions<WechatEventReplyOptions>? _replyOptions;

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
        // 模板解析链（4 级）：① Channel.Extra["news_reply"]（per-channel：静态投影/DB 动态权威层）
        // → ② Options.DefaultNewsReplyTemplate（全局默认）→ ③ EnableBuiltInTestTemplate 内置测试卡片（开发期）
        // → ④ null（纯文本模式——不回复图文）。①②③ 相互独立来源，逐级回退；全空且无内置 → null。
        var template = ReadMultiNewsTemplate(ctx.Channel?.Extra)
            ?? _replyOptions?.Value.DefaultNewsReplyTemplate
            ?? (_replyOptions?.Value.EnableBuiltInTestTemplate == true ? BuiltInTestTemplate : null);
        if (template is null or { Enabled: not true })
            return null;

        // v0.7.0 uid 绑定门（RequireUidForTicket=true 且编排层未富化 uid → 不回复——
        // 登录卡只发给已绑定平台账号的用户，对齐主包 LandingTicketHandler 门控语义）
        if (_replyOptions?.Value.RequireUidForTicket == true && string.IsNullOrEmpty(ctx.UId))
            return null;

        // I2-8：签一次免登票据（模板 TtlSeconds 优先 → FederationLoginEntryOptions.TicketTtlSeconds 缺省——ResolveTicketAsync 内；
        // 内置测试模板经 ctx.BaseUrl（请求 Host 推导）组装本站点免登链接——零配置链路）
        var ticketUrl = await ResolveTicketAsync(ctx, ct, template.TtlSeconds);

        // 占位符替换逐篇应用（{uid}/{openid}/{channel_id}/{channel_type}/{ticket}——I2-8 5 参重载）
        var articles = BuildArticles(template, ctx, ticketUrl);

        return new WechatReplyPayload(News: articles);
    }

    /// <summary>内置测试图文模板（开发期 <see cref="WechatEventReplyOptions.EnableBuiltInTestTemplate"/> 门控——
    /// 模板链①②均空时回退；标题"链路测试"、link 含 <c>{ticket}</c> 签票占位、pic_url 空 = 图片不存在）。</summary>
    private static readonly NewsReplyTemplateV2 BuiltInTestTemplate = new(
        Enabled: true,
        Articles: null,
        Title: "链路测试",
        Description: "点击验证完整回复链路",
        Link: "{ticket}",
        PicUrl: null,
        TtlSeconds: null);

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
