using TKW.Framework.Domain;

namespace TKWF.Ext.Federation;

/// <summary>
/// 联邦互联对外端点表现层配置（三层架构重构 T3 装配面——FederationWebExtension 配置分层，AGENTS §8 落地）。
/// <para><b>仅承载 HTTP 暴露面</b>：路由前缀 + 端点映射开关——<b>不镜像领域配置</b>
/// （<c>TrustCenterOptions</c>/<c>FederationChannelRegistryOptions</c> 等领域决策在对应领域节——如通道启用、
/// 密钥路径、令牌签发，Web 层不存在对应开关——防"Web 层关掉连接能力/密钥校验"绕过）。</para>
/// <para>端点开关默认 true = 暴露面默认开放；领域错误（如无默认通道 CHANNEL_NOT_FOUND / registry 故障
/// CHANNEL_REGISTRY_UNAVAILABLE / 事件验签失败）由领域门面经 SsoChannelAuthResult.FailReason 机器可读返回，
/// Web 层按映射表转 HTTP 状态码——安全由领域控制。</para>
/// <para>配置节：<c>TKWF:Federation:Web</c>——<c>[Options]</c> 由 SG1 消费方自动绑定（对齐
/// <see cref="AuthCenterEndpointOptions"/> 先例）。</para>
/// </summary>
[Options("TKWF:Federation:Web")]
public class FederationEndpointOptions
{
    /// <summary>对外端点路由前缀（默认 <c>/sso</c>——3 端点：login · login/{channelId} · jwks · event/{channelId} 挂此前缀下）。</summary>
    public string RoutePrefix { get; set; } = "/sso";

    /// <summary>登录编排端点开关（POST {prefix}/login · POST {prefix}/login/{channelId}——匿名游客帧）。</summary>
    public bool LoginEndpointEnabled { get; set; } = true;

    /// <summary>JWKS 公钥分发端点开关（GET {prefix}/jwks——匿名公开，信任内核公共密钥分发）。</summary>
    public bool JwksEndpointEnabled { get; set; } = true;

    /// <summary>平台事件推送接收端点开关（POST {prefix}/event/{channelId}——匿名，事件验签由平台库通道内部完成）。</summary>
    public bool EventEndpointEnabled { get; set; } = true;
}