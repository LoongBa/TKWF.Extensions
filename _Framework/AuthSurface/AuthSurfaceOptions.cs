using TKW.Framework.Domain;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 授权面配置选项（TKWF:AuthSurface 节——SG1 [Options] 自动绑定消费方配置节 + 默认值兜底）。
/// <para>领域决策（兑换尝试频控——安全策略）归领域层；Web 暴露面配置（未来管理端）走分层（AGENTS §8）。</para>
/// </summary>
[Options("TKWF:AuthSurface")]
public class AuthSurfaceOptions
{
    /// <summary>兑换尝试频控窗口（分钟，默认 60——用户维度无效码尝试计数窗口）。</summary>
    public int RedemptionAttemptWindowMinutes { get; set; } = 60;

    /// <summary>窗口内无效码尝试上限（默认 10——防批量枚举爆破；12 位 base32 码熵下 10 次爆破概率极低）。</summary>
    public int RedemptionAttemptMaxAttempts { get; set; } = 10;

    /// <summary>
    /// 附加信息（核验业务信息）AES-GCM 密钥文件路径（E4 keyed <see cref="TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider"/>——
    /// 键 AuthSurface，FileSymmetricKeyProvider 构造即加载：生产缺密钥 fail-fast / 开发随机兜底）。
    /// <para>null/空 = 未配置（仅开发允许随机兜底；生产必须配置——同 AuthCenter/Federation Options 语义）。</para>
    /// </summary>
    public string? SecretEncryptionKeyPath { get; set; }

    /// <summary>
    /// 生产模式（默认 true——对齐 AuthCenter/Federation/MFA Options：生产缺密钥拒绝启动，禁 dev 随机兜底；
    /// 开发环境置 false 允许自动生成随机密钥 + Warning）。
    /// </summary>
    public bool IsProduction { get; set; } = true;
}
