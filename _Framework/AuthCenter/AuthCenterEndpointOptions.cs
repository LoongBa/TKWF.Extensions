using TKW.Framework.Domain;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 认证中心对内端点表现层配置（V0.6.0 归层迭代 T4——配置分层原则 AGENTS §8 落地）。
/// <para><b>仅承载 HTTP 暴露面</b>：路由前缀 + 端点映射开关——<b>不镜像领域配置</b>
/// （<c>EnabledAuthTypes</c>/<c>LoginProtection</c> 等领域安全行为在 <see cref="AuthCenterOptions"/>
/// <c>TKWF:AuthCenter</c> 节，Web 层不存在对应开关——防"Web 层关掉短信频控/口令校验"绕过）。</para>
/// <para>端点开关默认 true = 暴露面默认开放；若领域 <c>EnabledAuthTypes</c> 未启用对应 Provider →
/// 端点暴露但调用时领域层 fail-closed 拒绝（安全由领域控制，非不一致状态，指南标注）。</para>
/// <para>配置节：<c>TKWF:AuthCenter:Web</c>——<c>[Options]</c> 由 SG1 消费方自动绑定（对齐
/// <see cref="AuthCenterOptions"/> 先例）；命名对齐 HealthCheck <c>HealthCheckEndpointOptions</c> 先例
/// （<c>AuthCenterEndpointOptions</c>——避与 <c>AuthCenterOptions</c> 混淆）。</para>
/// </summary>
[Options("TKWF:AuthCenter:Web")]
public class AuthCenterEndpointOptions
{
    /// <summary>对内端点路由前缀（默认 <c>/api/auth</c>——6 端点：sms/send-code · login/sms · login/wechat · refresh · logout · ticket/exchange 挂此前缀下）。</summary>
    public string RoutePrefix { get; set; } = "/api/auth";

    /// <summary>短信验证码发送端点开关（POST {prefix}/sms/send-code）。</summary>
    public bool SmsSendCodeEndpointEnabled { get; set; } = true;

    /// <summary>短信登录端点开关（POST {prefix}/login/sms）。</summary>
    public bool SmsLoginEndpointEnabled { get; set; } = true;

    /// <summary>微信登录端点开关（POST {prefix}/login/wechat）。</summary>
    public bool WechatLoginEndpointEnabled { get; set; } = true;

    /// <summary>刷新端点开关（POST {prefix}/refresh）。</summary>
    public bool RefreshEndpointEnabled { get; set; } = true;

    /// <summary>登出端点开关（POST {prefix}/logout——需 Bearer 认证）。</summary>
    public bool LogoutEndpointEnabled { get; set; } = true;

    /// <summary>票据换令牌端点开关（POST {prefix}/ticket/exchange）。</summary>
    public bool TicketExchangeEndpointEnabled { get; set; } = true;

    /// <summary>令牌验签快照端点开关（GET {prefix}/verify——已认证内省，Bearer 必需；Oracle P2-3 前缀消歧义）。</summary>
    public bool TokenVerifyEndpointEnabled { get; set; } = true;

    /// <summary>应用授权状态端点开关（GET {prefix}/grants——已认证 Bearer，Bearer 必需）。</summary>
    public bool GrantsEndpointEnabled { get; set; } = true;

    /// <summary>验证码独立校验端点开关（POST {prefix}/sms/verify——匿名，scene 白名单 Login/Register/Bind/Reset）。</summary>
    public bool SmsVerifyEndpointEnabled { get; set; } = true;
}
