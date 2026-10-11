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
    /// <summary>对外端点路由前缀（默认 <c>/feberation</c>——平台集成面 6 端点：{platformId}/login[ · /{channelId}] ·
    /// {platformId}/oauth[/{channelId}]/callback · {platformId}/event[/{channelId}] + 信任/委托面 4 端点：
    /// jwks · authorize/start · trust/issue · identity/claim 挂此前缀下；<c>/feberation</c> 对齐中文名「联邦互联」，
    /// 二级段语义分流——平台族（wechat/dingtalk/...）vs 端点名（jwks/authorize/...），见
    /// <c>docs/Federation/对外路由命名空间-开发方案.md</c>）。</summary>
    public string RoutePrefix { get; set; } = "/feberation";

    /// <summary>登录编排端点开关（POST {prefix}/{platformId}/login · POST {prefix}/{platformId}/login/{channelId}——匿名游客帧）。</summary>
    public bool LoginEndpointEnabled { get; set; } = true;

    /// <summary>JWKS 公钥分发端点开关（GET {prefix}/jwks——匿名公开，信任内核公共密钥分发；跨平台根级不挂平台段）。</summary>
    public bool JwksEndpointEnabled { get; set; } = true;

    /// <summary>平台事件推送接收端点开关（POST {prefix}/{platformId}/event[/{channelId}]——匿名，事件验签由平台库通道内部完成；
    /// channelType 按平台段推导 <c>{PlatformType}_event</c>——修复 HandleEvent null 推导缺陷）。</summary>
    public bool EventEndpointEnabled { get; set; } = true;

    /// <summary>子应用发起端点开关（POST {prefix}/authorize/start——匿名 + 建议消费方装配 RateLimitingWebExtension IP 限流，Oracle 条件 5）。</summary>
    public bool AuthorizeEndpointEnabled { get; set; } = true;

    /// <summary>OAuth 回调落地端点开关（GET {prefix}/{platformId}/oauth[/{channelId}]/callback——匿名，平台 IdP 302 回调落点；须 GET——IdP 重定向语义，Oracle 条件 1）。</summary>
    public bool OauthCallbackEndpointEnabled { get; set; } = true;

    /// <summary>Platform 登录态签发端点开关（POST {prefix}/trust/issue——已认证 Bearer token1；direct 模式；须 AuthCenterWebExtension 同装配）。</summary>
    public bool TrustIssueEndpointEnabled { get; set; } = true;

    /// <summary>子应用兑现端点开关（POST {prefix}/identity/claim——匿名 + 子应用 credential 鉴权；统一 401 防枚举，Oracle 条件 4）。</summary>
    public bool IdentityClaimEndpointEnabled { get; set; } = true;
}