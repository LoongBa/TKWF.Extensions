using System;
using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Ext.Federation;

/// <summary>SSO 签名密钥配置（kid 轮换——JWK RFC 7517 语义；EC P-256）。</summary>
public class FederationSigningKeyConfig
{
    /// <summary>密钥版本标识（JWT header kid）。</summary>
    public string Kid { get; set; } = "";

    /// <summary>EC 私钥 PEM 文件路径（PKCS#8——签发用）。</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>公钥 PEM 文件路径（可选，默认从私钥推导）。</summary>
    public string? PublicKeyPath { get; set; }
}

/// <summary>
/// SSO 联邦层配置选项（TKWF:Federation 节）。
/// <para>[Options("TKWF:Federation")]：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定
/// （对齐 AuthCenterOptions 先例——模式 A 双通道）。</para>
/// <para>关键安全门：生产（IsProduction=true）缺签名密钥 → 拒绝启动（fail-fast，对齐 Authentication）；
/// 开发自动生成临时 EC 密钥 + Warning（DevEcKeyCache 进程内缓存）。</para>
/// </summary>
[Options("TKWF:Federation")]
public class FederationOptions
{
    /// <summary>token2 签发者（iss——SSO 实例标识，如 https://api.lexue.loongba.cn）。</summary>
    public string Issuer { get; set; } = "";

    /// <summary>EC 私钥 PEM 路径（当前签发密钥，PKCS#8；fail-fast 门）。</summary>
    public string? SigningKeyPath { get; set; }

    /// <summary>当前签发密钥 kid（SigningKeys 中匹配）。</summary>
    public string CurrentKid { get; set; } = "sso-key-1";

    /// <summary>签名密钥集合（kid 轮换——验证遍历所有 kid 匹配）。</summary>
    public List<FederationSigningKeyConfig> SigningKeys { get; set; } = [];

    /// <summary>token2 有效期（秒，默认 300——设计文档 §6.2）。</summary>
    public int Token2ExpirationSeconds { get; set; } = 300;

    /// <summary>验签 leeway（秒，默认 30——设计文档 §5.6）。</summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>accesscode 有效期（秒，默认 120——Oracle P2-2：60s 偏紧，点击延迟）。</summary>
    public int AccessCodeExpirationSeconds { get; set; } = 120;

    /// <summary>app client credential / HMAC 密钥 AES-GCM 加密密钥派生文件路径（对齐 AuthCenter SecretEncryptionKeyPath）。</summary>
    public string? SecretEncryptionKeyPath { get; set; }

    /// <summary>生产标记（true = fail-fast 门——缺密钥/默认值拒绝启动；开发自动生成 + Warning）。</summary>
    public bool IsProduction { get; set; }
}
