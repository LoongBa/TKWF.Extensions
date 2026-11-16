using TKW.Framework.Domain;

namespace TKWF.Ext.Captcha;

/// <summary>
/// 人机验证配置（<c>TKWF:Captcha</c> 节——方案 §5.5，领域配置；Web 层不存在镜像开关防绕过）。
/// <para>fail-closed（m7 评审闭环）：<see cref="Enabled"/>=false 与消费方场景要求门禁（RequiresCaptcha=true）冲突时，
/// 门面恒抛守卫异常（禁静默放行——防运维误关导致短信预算裸奔）；「开关可 off」仅允许场景注册表不要求门禁的场景。</para>
/// </summary>
[Options("TKWF:Captcha")]
public class CaptchaOptions
{
    /// <summary>全局开关（默认 true——fail-closed：true 缺 Provider/校验失败 → 拒绝；false 仅 dev）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>启用的 Provider 类型（Options 精确匹配 <see cref="ICaptchaProvider.Type"/>，未匹配 → fail-closed 异常）。</summary>
    public string EnabledProviderType { get; set; } = "image";

    /// <summary>挑战有效期（秒，默认 300 = 5min——对齐 SmsCode/MFA ChallengeTtl 先例）。</summary>
    public int ChallengeTtlSeconds { get; set; } = 300;

    /// <summary>单挑战答错上限（默认 3——达上限作废该挑战）。</summary>
    public int AttemptsPerChallenge { get; set; } = 3;

    /// <summary>取题频控：每 IP 每分钟最大签发数（默认 10——防挑战表洪泛）。</summary>
    public int IssuePerMinutePerIp { get; set; } = 10;
}