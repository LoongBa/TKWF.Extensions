using System;
using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Ext.AuthCenter;

/// <summary>验签模式（方案 §5.8/§5.11——VerifyMode）。</summary>
public enum VerifyMode
{
    /// <summary>本地公钥验签（默认——共享公钥本地验签，零网络依赖）。</summary>
    LocalPublicKey = 0,

    /// <summary>远程 introspection（装配层替换 ITokenVerifier 实现）。</summary>
    RemoteIntrospection = 1
}

/// <summary>签名密钥配置（方案 §5.4——kid 轮换：JWK RFC 7517 语义，旧密钥签名可验证直到过期）。</summary>
public class SigningKeyConfig
{
    /// <summary>密钥版本标识（JWT header kid）。</summary>
    public string Kid { get; set; } = "";

    /// <summary>RSA 私钥 PEM 文件路径（签发用）。</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>公钥 PEM 文件路径（可选，默认从私钥推导）。</summary>
    public string? PublicKeyPath { get; set; }
}

/// <summary>登录保护策略配置（方案 §5.6——各认证方式窗口/阈值；对齐需求 §5.4 Oracle N3 补全）。</summary>
public class LoginProtectionOptions
{
    /// <summary>短信重发最小间隔（秒，默认 60）。</summary>
    public int SmsResendIntervalSeconds { get; set; } = 60;

    /// <summary>短信每小时每手机号上限（默认 5）。</summary>
    public int SmsHourlyLimitPerPhone { get; set; } = 5;

    /// <summary>短信每天每手机号上限（默认 20）。</summary>
    public int SmsDailyLimitPerPhone { get; set; } = 20;

    /// <summary>短信每小时每 IP 上限（默认 20）。</summary>
    public int SmsHourlyLimitPerIp { get; set; } = 20;

    /// <summary>短信校验每账号每小时尝试上限（默认 5——校验频控）。</summary>
    public int SmsVerifyAttemptsPerHour { get; set; } = 5;

    /// <summary>OAuth 每分钟每 IP 尝试上限（默认 10，AuthLoginAttempt 计数）。</summary>
    public int OAuthPerMinutePerIp { get; set; } = 10;

    /// <summary>口令兑换每小时尝试上限（默认 5）。</summary>
    public int RedeemPerHour { get; set; } = 5;
}

/// <summary>
/// 认证中心配置选项（方案 §5.11——TKWF:AuthCenter 节）。
/// <para>[Options("TKWF:AuthCenter")]：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定
/// （对齐 Settings/Navigation/Permissions 先例——模式 A 双通道；亦可在消费方 ConfigureExtensions 编程覆盖）。</para>
/// <para>关键安全门：生产（IsProduction=true）缺签名密钥/默认值 → 拒绝启动（fail-fast，对齐 DMP）；开发自动生成临时密钥 + Warning。</para>
/// </summary>
[Options("TKWF:AuthCenter")]
public class AuthCenterOptions
{
    /// <summary>令牌签发者（iss——auth-instance-id，多实例隔离校验）。</summary>
    public string Issuer { get; set; } = "";

    /// <summary>RSA 私钥 PEM 路径（当前签发密钥；fail-fast 门）。</summary>
    public string? SigningKeyPath { get; set; }

    /// <summary>公钥 PEM 路径（可选，默认从私钥推导）。</summary>
    public string? PublicKeyPath { get; set; }

    /// <summary>当前签发密钥 kid（SigningKeys 中匹配）。</summary>
    public string CurrentKid { get; set; } = "rsa-key-1";

    /// <summary>签名密钥集合（kid 轮换——验证遍历所有 kid 匹配）。</summary>
    public List<SigningKeyConfig> SigningKeys { get; set; } = [];

    /// <summary>Access Token 有效期（分钟，默认 120）。</summary>
    public int AccessTokenExpirationMinutes { get; set; } = 120;

    /// <summary>Long-lived Token 有效期（天，默认 7——壳端/低敏）。</summary>
    public int LongLivedTokenExpirationDays { get; set; } = 7;

    /// <summary>Refresh Token 有效期（天，默认 30，rotation）。</summary>
    public int RefreshTokenExpirationDays { get; set; } = 30;

    /// <summary>一次性票据有效期（分钟，默认 5）。</summary>
    public int TicketExpirationMinutes { get; set; } = 5;

    /// <summary>启用认证方式（fail-closed——集合外 Provider 不接线；默认 ["sms"]）。</summary>
    public string[] EnabledAuthTypes { get; set; } = ["sms"];

    /// <summary>票据换取 redirect_uri 白名单（app_id 校验 + 白名单——防跨应用抢先消费）。</summary>
    public string[] RedirectUriWhitelist { get; set; } = [];

    /// <summary>平台凭证 AES-GCM 密钥派生文件路径（PlatformCredential 加解密）。</summary>
    public string? SecretEncryptionKeyPath { get; set; }

    /// <summary>验签模式（默认本地公钥）。</summary>
    public VerifyMode VerifyMode { get; set; } = VerifyMode.LocalPublicKey;

    /// <summary>登录保护策略（各认证方式窗口/阈值）。</summary>
    public LoginProtectionOptions LoginProtection { get; set; } = new();

    /// <summary>生产标记（true = fail-fast 门——缺密钥/默认值拒绝启动；开发自动生成 + Warning）。</summary>
    public bool IsProduction { get; set; }
}
