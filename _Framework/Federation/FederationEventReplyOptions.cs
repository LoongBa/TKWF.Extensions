namespace TKWF.Ext.Federation;

/// <summary>
/// 事件回复领域配置（TKWF:Federation:EventReply）——配置分层 AGENTS §8：领域行为开关归领域节
/// （Web 暴露面开关归 <see cref="FederationEndpointOptions"/>，POCO 不镜像）。
/// <para><see cref="TestTicketEndpointBaseUrl"/> 与 <see cref="RoutePrefix"/> 供
/// <see cref="EventReplyHandlerBase"/>（签密封票据 → 测试端点图文链接拼接）消费——测试端点是 Federation
/// 主包编排面能力（方案 §3.5）。</para>
/// <para>内置测试 handler 注册开关 <c>TestEventReplyHandlerEnabled</c> 已迁平台库
/// （<c>TKWF.Federation.WeChat</c> 的 <c>WechatEventReplyOptions.ProbeEventReplyHandlerEnabled</c>——
/// 方案 §3.6 主包删除，2026-10-17 用户裁定不保留过渡）。</para>
/// </summary>
public class FederationEventReplyOptions
{
    /// <summary>
    /// 测试端点 URL 基址（消费方部署域名——图文链接拼接用，如 <c>https://api.example.com</c>；
    /// 空 = 相对路径 <c>/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}</c>（以 / 开头，默认指向本服务同源——微信可点击）。
    /// </summary>
    public string? TestTicketEndpointBaseUrl { get; set; }

    /// <summary>测试端点 RoutePrefix（默认 /feberation——与 <see cref="FederationEndpointOptions.RoutePrefix"/> 一致）。</summary>
    public string RoutePrefix { get; set; } = "/feberation";

    /// <summary>
    /// S2 未知通道是否以 200 空响应替代 404（D03 §4.3 S2；默认 <c>false</c> = 404 静默防探测）。
    /// <para>微信对非 200 响应会重试（重试风暴风险）——开启后未知通道返回 200 空回复，
    /// 探测者亦无法区分「通道不存在」与「通道存在但无回复」。硬线不可配的是 S1 验签失败（一票否决）。</para>
    /// </summary>
    public bool UnknownChannelReplyEnabled { get; set; }

    /// <summary>
    /// S3 通道停用时应提示的文案（D03 §4.3 S3；默认「服务暂停中，请稍后再试」）。
    /// <para><b>渲染边界</b>：微信被动回复 XML 信封的唯一权威是平台库
    /// （<c>TKWF.Federation.WeChat</c> 的 <c>BuildTextReply</c>）与回复 handler 层——Federation 主包
    /// 不构造 XML（P9 零重复、P2 单一事实源）。本项供 handler / 装配层读取渲染；事件端点自身按
    /// 200 空回复处理（微信不重试），文案值随 Warning 日志输出便于运维观测。</para>
    /// </summary>
    public string DisabledChannelReplyTemplate { get; set; } = "服务暂停中，请稍后再试";

    /// <summary>
    /// S5 无 handler 命中时的兜底回复文案（D03 §4.3 S5；默认「感谢您的关注，请稍后再试」）。
    /// <para><b>优先级</b>：WeChatOperations 回复 handler 的模板兜底（<c>WeChatOperationsOptions.DefaultReplyText</c>）
    /// 先于本项执行——本项仅在 handler 未装配/未命中时由装配层兜底。渲染边界同
    /// <see cref="DisabledChannelReplyTemplate"/>（Federation 主包不构造 XML）。</para>
    /// </summary>
    public string DefaultReplyText { get; set; } = "感谢您的关注，请稍后再试";
}
