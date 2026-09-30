namespace TKWF.Ext.MFA;

/// <summary>
/// 多因素认证配置选项。
/// <para>通过 <c>services.AddOptions&lt;MfaOptions&gt;().BindConfiguration("TKWF:Mfa")</c> 绑定（模式 A 双通道）。</para>
/// <para>Oracle 评审（bg_025c18fc）Q7 全部默认值确认合理。</para>
/// </summary>
public class MfaOptions
{
    /// <summary>挑战票据/SMS 码 TTL（秒，默认 5 分钟——对齐 OAuthTicket/SmsRecord 同窗）。</summary>
    public int ChallengeTtlSeconds { get; set; } = 300;

    /// <summary>TOTP 时间步（秒，RFC 6238 标准 30）。</summary>
    public int TotpTimeStepSeconds { get; set; } = 30;

    /// <summary>TOTP 码位数（RFC 6238 标准 6）。</summary>
    public int TotpDigits { get; set; } = 6;

    /// <summary>TOTP 前后容差窗口（±1 步防时钟漂移，对齐 Google Authenticator）。</summary>
    public int TotpClockSkewWindows { get; set; } = 1;

    /// <summary>验证尝试频控上限（滑动窗口内最大尝试次数，默认 5 次）。</summary>
    public int MaxVerifyAttemptsPerWindow { get; set; } = 5;

    /// <summary>验证尝试频控窗口（分钟，默认 5 分钟）。</summary>
    public int VerifyAttemptWindowMinutes { get; set; } = 5;

    /// <summary>短信发码每用户小时上限（默认 5 条——⚠️ 多实例部署内存窗口不跨实例，须外部限流器或单实例，Oracle C8）。</summary>
    public int SmsMaxPerHour { get; set; } = 5;

    /// <summary>恢复码数量（默认 8——Google/ABP 10 码同量级；格式 8 位字母数字去易混淆字符）。</summary>
    public int RecoveryCodeCount { get; set; } = 8;

    /// <summary>
    /// MFA secret AES-GCM 密钥派生文件路径（TOTP secret 加解密；前 32 字节）。
    /// <para>⚠️ 生产（<see cref="IsProduction"/>）缺此配置 → fail-fast 拒启动（对齐 Authentication SecretEncryptionKeyPath 策略）。</para>
    /// </summary>
    public string? SecretEncryptionKeyPath { get; set; }

    /// <summary>生产环境门（true = 密钥缺失 fail-fast 拒启动；false = 开发生成临时密钥 + Warning）。</summary>
    public bool IsProduction { get; set; }
}
