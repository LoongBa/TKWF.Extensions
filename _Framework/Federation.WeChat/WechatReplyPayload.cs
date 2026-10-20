using System.Collections.Generic;
using System.Text.Json.Serialization;

using TKWF.Ext.Federation;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 业务回复载荷（方案 §3.2 返回载荷类型——基类负责包微信 XML 信封，子类零 XML 感知）。
/// <para>语义：<see cref="Content"/> 文本回复（优先）；<see cref="News"/> 图文回复
/// （多图文——<c>Content</c> 为空时用，基类 <see cref="WechatEventReplyHandlerBase.BuildReplyResult"/>
/// 组装 news 信封时优先）。两者均为 null → 基类返回 null（不占 first-non-null-wins 槽——其它 handler 继续）。</para>
/// <para>⚠️ V0.4.2 <see cref="RawXml"/> 逃逸口（Oracle P0-1 选 A）：非空时
/// <see cref="WechatEventReplyHandlerBase.BuildReplyResult"/> <b>直接返回该原始 XML</b>
/// （跳过 text/news 重组——SyncProxy 下游自组信封 XML 原样回复，防双重信封）；默认 null 存量零破坏。
/// 优先级：<b>RawXml &gt; News &gt; Content</b>。</para>
/// <para>消费方形态（方案 §3.7）：分支虚方法返回 <see cref="WechatReplyPayload"/> 或 null（不回复）。
/// 子类构造见 <see cref="EventReplyNewsArticle"/>（Title/Description/Url/PicUrl——微信图文条目）。</para>
/// </summary>
/// <param name="Content">文本回复内容（优先于 <see cref="News"/>；null = 无文本）。</param>
/// <param name="News">图文回复条目列表（多图文——<c>Content</c> 为空时用；null = 无图文）。</param>
/// <param name="RawXml">原始 XML 透传逃逸口（V0.4.2 Oracle P0-1 选 A——完整微信被动回复 XML，
/// 下游自组信封；非空时 <see cref="WechatEventReplyHandlerBase.BuildReplyResult"/> 直接返回该 XML，
/// 跳过重组防双重信封；null = 不使用）。</param>
public sealed record WechatReplyPayload(
    string? Content = null,
    IReadOnlyList<EventReplyNewsArticle>? News = null,
    string? RawXml = null);

/// <summary>
/// 图文模板条目（<c>Channel.Extra["news_reply"]</c> JSON <c>articles</c> 数组元素——微信图文条目，
/// 占位符可替换；V0.4.1 多图文模板结构）。
/// </summary>
public sealed record NewsReplyArticle(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("pic_url")] string? PicUrl);

/// <summary>
/// 多图文模板 DTO（<c>Channel.Extra["news_reply"]</c> JSON——V0.4.1
/// <see cref="WechatTemplateEventReplyHandler"/> 升级，方案 §3.2.3）。
/// <para><b>articles 数组优先</b>（多图文——1..8 篇），<b>缺省回退单图文字段</b>（存量兼容零破坏）：</para>
/// <code>
/// // 多图文形态
/// { "enabled": true, "articles": [ { "title": "...", "description": "...", "link": "...", "pic_url": "..." }, ... ] }
/// // 单图文兼容形态（缺 articles 字段）
/// { "enabled": true, "title": "...", "description": "...", "link": "...", "pic_url": "..." }
/// </code>
/// <para><see cref="TtlSeconds"/> 保留字段一致性（无签票时无业务语义，忽略——对齐平台基类
/// <see cref="WechatEventReplyHandlerBase.NewsReplyTemplate"/>）。</para>
/// </summary>
public sealed record NewsReplyTemplateV2(
    [property: JsonPropertyName("enabled")] bool? Enabled,
    [property: JsonPropertyName("articles")] IReadOnlyList<NewsReplyArticle>? Articles,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("pic_url")] string? PicUrl,
    [property: JsonPropertyName("ttl_seconds")] int? TtlSeconds);