namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 认证强度等级——令牌 <c>auth_level</c> 声明与账号认证强度的取值。
/// <para>V0.9.0 泛化（ADR-AuthCenter-身份域数据模型与密码能力边界 A.3）：1=手机号级 / 2=联邦快捷认证
/// （微信/QQ/支付宝扫码、OIDC 联邦登录——不写死平台，平台差异归 <c>AuthType</c>/<c>ChannelId</c>）；
/// 剔除 3=教师核实（TeacherVerified 迁出——令牌不再携带业务声明，教师核实走教育线业务扩展）。</para>
/// </summary>
public enum AuthLevel
{
    /// <summary>手机号（短信验证码主认证路径；密码登录亦 1——凭据强度）。</summary>
    Phone = 1,

    /// <summary>联邦快捷认证（微信/QQ/支付宝/OIDC 等快捷登录——平台差异归 AuthType/ChannelId，原 Wechat=2 泛化 renamed）。</summary>
    Federated = 2
}

/// <summary>认证方式常量——令牌 <c>authType</c> 声明与 Provider AuthType 取值（方案 §5.2/§5.5）。</summary>
public static class AuthTypes
{
    /// <summary>短信验证码（内置 Provider）。</summary>
    public const string Sms = "sms";

    /// <summary>微信 OAuth（内置 Provider，双形态）。</summary>
    public const string Wechat = "wechat";

    /// <summary>密码（V0.9.0 B.9 落地：PasswordAuthenticationProvider——扩展点实现启用）。</summary>
    public const string Password = "password";

    /// <summary>口令兑换（教育线特有，扩展点）。</summary>
    public const string Redeem = "redeem";

    /// <summary>抖音 OAuth（DMP 生活服务电商迁移时装配，扩展点）。</summary>
    public const string Douyin = "douyin";
}
