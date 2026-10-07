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
}
