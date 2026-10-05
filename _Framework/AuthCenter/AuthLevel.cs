namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 认证强度等级——令牌 <c>auth_level</c> 声明与账号认证强度的取值。
/// <para>1=手机号 / 2=微信便捷 / 3=教师核实（对应 AuthAccount.AuthLevel 列，方案 §5.3）。</para>
/// </summary>
public enum AuthLevel
{
    /// <summary>手机号（短信验证码主认证路径）。</summary>
    Phone = 1,

    /// <summary>微信便捷（网页授权/扫码 openid 归并）。</summary>
    Wechat = 2,

    /// <summary>教师核实（认证中心仅标「已核实教师」，不含业务权限）。</summary>
    Teacher = 3
}

/// <summary>认证方式常量——令牌 <c>authType</c> 声明与 Provider AuthType 取值（方案 §5.2/§5.5）。</summary>
public static class AuthTypes
{
    /// <summary>短信验证码（内置 Provider）。</summary>
    public const string Sms = "sms";

    /// <summary>微信 OAuth（内置 Provider，双形态）。</summary>
    public const string Wechat = "wechat";

    /// <summary>密码（Provider 扩展点，本期预留列）。</summary>
    public const string Password = "password";

    /// <summary>口令兑换（教育线特有，扩展点）。</summary>
    public const string Redeem = "redeem";

    /// <summary>抖音 OAuth（DMP 生活服务电商迁移时装配，扩展点）。</summary>
    public const string Douyin = "douyin";
}
