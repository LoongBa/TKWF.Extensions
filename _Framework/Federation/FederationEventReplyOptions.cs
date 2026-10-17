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
}
