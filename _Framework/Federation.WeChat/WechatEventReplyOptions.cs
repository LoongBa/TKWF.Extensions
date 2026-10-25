using TKW.Framework.Domain;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信事件回复平台层领域 Options（<c>TKWF:Federation:WeChat:EventReply</c> 节——探针 handler 开关）。
/// <para><b>配置分层（AGENTS §8 领域自治——2026-10-06 用户裁定）</b>：探针开关改变<b>领域行为</b>
/// （是否启用诊断探针回复）→ 归领域节 <c>TKWF:Federation:WeChat:EventReply</c>，Web/表现层
/// <b>不存在</b>对应开关（防"Web 层关掉/打开领域能力"绕过）。</para>
/// <para>方案 §3.5：<see cref="ProbeEventReplyHandlerEnabled"/> 默认 <c>false</c>（Oracle P1-1 fail-safe）——
/// 开发期开启 true 验证全分支；接入业务 handler 后保持 false（探针与业务 handler 互斥——Oracle P2-5）。
/// 注册：<c>AddWeChatFederationChannels()</c> 内 <c>AddOptions&lt;WechatEventReplyOptions&gt;()</c> 兜底默认值
/// + SG1 [Options] 在消费方自动绑定（对齐 <see cref="WeChatOptions"/> 双通道模式）。</para>
/// </summary>
[Options("TKWF:Federation:WeChat:EventReply")]
public class WechatEventReplyOptions
{
    /// <summary>
    /// 探针 handler 启用开关（默认 <c>false</c>——Oracle P1-1 fail-safe）。
    /// <para>false（默认）：<see cref="WechatProbeEventReplyHandler"/> 全分支返回 null（不回复不干预，
    /// 业务 handler 正常流转）；true：可回复分支返回 <c>PROBE:branch=...</c> 可区分文本
    /// （诊断验证各分支命中），门控分支由基类协议门控吞掉 + ILogger 输出。</para>
    /// </summary>
    public bool ProbeEventReplyHandlerEnabled { get; set; }

    /// <summary>
    /// 免登票据 uid 绑定门（默认 <c>false</c>——v0.7.0 排他单模型认证语义）。
    /// <para><c>true</c>：<see cref="WechatTemplateEventReplyHandler"/> 仅在编排层已富化 uid
    /// （openid 已绑定平台账号）时签 <c>{ticket}</c> 免登票据；未绑定（<c>ctx.UId</c> 空）→
    /// 跳过签票（<c>{ticket}</c> 占位符替换为空串，链接不生成）——登录卡只发给已绑定用户，
    /// 对齐主包 <c>LandingTicketHandler</c> 的 uid 门控语义（防未绑定 openid 拿到登录链接）。</para>
    /// <para><c>false</c>（默认）：通用模板场景——任意用户触发均可签票（票据身份为 openid 级，
    /// 兑换侧实时回查 uid）。</para>
    /// </summary>
    public bool RequireUidForTicket { get; set; }

    /// <summary>
    /// 全局默认图文回复模板（<c>null</c> = 未配置——模板解析链第二级兜底）。
    /// <para>模板解析链（<see cref="WechatTemplateEventReplyHandler.OnDefaultAsync"/>）：
    /// ① <c>Channel.Extra["news_reply"]</c>（per-channel：静态投影 / DB 动态权威层）→
    /// ② 本全局默认 → ③ <see cref="EnableBuiltInTestTemplate"/> 内置测试卡片（开发期）→ ④ null（纯文本模式）。</para>
    /// <para>配置形态：<c>TKWF:Federation:WeChat:EventReply:DefaultNewsReplyTemplate</c> 段——
    /// 同 <see cref="NewsReplyTemplateV2"/> JSON 结构（enabled/articles/title/...，snake_case 绑定）。</para>
    /// </summary>
    public NewsReplyTemplateV2? DefaultNewsReplyTemplate { get; set; }

    /// <summary>
    /// 内置测试图文模板开关（默认 <c>false</c>——fail-safe：模板链①②均空时<b>不</b>自动回退内置卡片）。
    /// <para><c>true</c>：模板解析链 ①② 均空时回退内置单图文卡片（标题"链路测试"、link 含 <c>{ticket}</c>
    /// 签票占位、pic_url 空 = 图片不存在）——装配即测微信图文 + ticket 闭环，零 seed 零管理端
    /// （开发期验证链路走通）。<b>开发联调配置 <c>appsettings.Development.json</c>（随仓库提交），
    /// 生产 appsettings.json 不配（默认 false）——绝不发"链路测试"卡片给真实用户。</b></para>
    /// </summary>
    public bool EnableBuiltInTestTemplate { get; set; }
}