using System.Collections.Generic;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 业务回复载荷（方案 §3.2 返回载荷类型——基类负责包微信 XML 信封，子类零 XML 感知）。
/// <para>语义：<see cref="Content"/> 文本回复（优先）；<see cref="News"/> 图文回复
/// （多图文——<c>Content</c> 为空时用，基类 <see cref="WechatEventReplyHandlerBase.BuildReplyResult"/>
/// 组装 news 信封时优先）。两者均为 null → 基类返回 null（不占 first-non-null-wins 槽——其它 handler 继续）。</para>
/// <para>消费方形态（方案 §3.7）：分支虚方法返回 <see cref="WechatReplyPayload"/> 或 null（不回复）。
/// 子类构造见 <see cref="EventReplyNewsArticle"/>（Title/Description/Url/PicUrl——微信图文条目）。</para>
/// </summary>
/// <param name="Content">文本回复内容（优先于 <see cref="News"/>；null = 无文本）。</param>
/// <param name="News">图文回复条目列表（多图文——<c>Content</c> 为空时用；null = 无图文）。</param>
public sealed record WechatReplyPayload(
    string? Content = null,
    IReadOnlyList<EventReplyNewsArticle>? News = null);