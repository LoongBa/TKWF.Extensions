namespace TKWF.Ext.Federation;

/// <summary>
/// 事件回复领域配置（TKWF:Federation:EventReply）——配置分层 AGENTS §8：领域行为开关归领域节
/// （Web 暴露面开关归 <see cref="FederationEndpointOptions"/>，POCO 不镜像）。
/// <para>内置测试 handler 生命周期：开发期测试 <c>TestEventReplyHandlerEnabled=true</c> +
/// <c>TestAccessCodeEndpointBaseUrl</c> 配部署域名 + 测试端点开
/// （<c>TKWF:Federation:Web:TestAccessCodeEndpointEnabled=true</c>）；接入业务 handler 后
/// <b>测试开关保持关闭</b>（测试设施整体不启用——使用指南"测试设施生命周期"小节）。</para>
/// </summary>
public class FederationEventReplyOptions
{
    /// <summary>
    /// 内置测试 handler 注册开关（默认 false——P1-1 fail-safe 显式开启；与测试端点
    /// <c>TestAccessCodeEndpointEnabled</c> 默认一致）。消费方接入业务 handler 后保持关闭。
    /// </summary>
    public bool TestEventReplyHandlerEnabled { get; set; } = false;

    /// <summary>
    /// 测试端点 URL 基址（消费方部署域名——图文链接拼接用，如 <c>https://api.example.com</c>；
    /// 空 = 测试 handler 不回复图文（返回 null，不拦截业务 handler——P1-2/P2-1）。
    /// </summary>
    public string? TestAccessCodeEndpointBaseUrl { get; set; }

    /// <summary>测试端点 RoutePrefix（默认 /feberation——与 <see cref="FederationEndpointOptions.RoutePrefix"/> 一致）。</summary>
    public string RoutePrefix { get; set; } = "/feberation";
}
