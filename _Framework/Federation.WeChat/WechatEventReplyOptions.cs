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
}