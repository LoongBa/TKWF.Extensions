using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Ext.SmsCode
{
    /// <summary>
    /// 短信验证码扩展配置（<c>TKWF:SmsCode</c> 节——领域配置，Web 层不可关闭，配置分层 AGENTS §8）。
    /// <para><b>b+ 共识</b>：场景注册表驱动——每场景策略（门禁开关/未来 per-scene 覆盖），零 DMP 逻辑。</para>
    /// <para>⚠️ <b>门禁默认值</b>：login 场景 <c>RequiresCaptcha=true</c>（M3 门禁内联接线完成态——Q6/m7 闭环）：
    /// 场景要求门禁时，Captcha 未装配 → 守卫异常（fail-closed，禁静默跳过）。关闭门禁仅允许"场景注册表不要求"
    /// 的场景（每场景显式 false）；全局 <c>Captcha.Enabled=false</c> 与场景要求门禁冲突 → 恒守卫异常。</para>
    /// <para>频控默认值 = oracle2 冻结版（比 AuthCenter 收紧：日 10 / 时 5 / IP 20）。</para>
    /// </summary>
    [Options("TKWF:SmsCode")]
    public class SmsCodeOptions
    {
        /// <summary>同 subject+scene 重发间隔（秒）。</summary>
        public int SmsResendIntervalSeconds { get; set; } = 60;

        /// <summary>每主体日上限（oracle2 冻结：比 AuthCenter 20 收紧）。</summary>
        public int SmsDailyLimitPerSubject { get; set; } = 10;

        /// <summary>每主体小时上限。</summary>
        public int SmsHourlyLimitPerSubject { get; set; } = 5;

        /// <summary>IP 桶小时上限（send+verify 共享，IRateLimitCheck 点检查）。</summary>
        public int SmsHourlyLimitPerIp { get; set; } = 20;

        /// <summary>活跃未消费码上限（防轰炸发送；只统计未失效记录——重发作废旧码机制 M3 闭环）。</summary>
        public int MaxActiveCodesPerSubject { get; set; } = 5;

        /// <summary>校验尝试上限（15min 窗，计数 verify 调用而非活跃记录——修正 AuthCenter 缺陷落点，M3 闭环）。</summary>
        public int VerifyAttemptsPerWindow { get; set; } = 5;

        /// <summary>同码连续失败 N 次作废该码（FailedAttempts 列 CAS 自增，M3 闭环）。</summary>
        public int FailedVerifyInvalidateThreshold { get; set; } = 5;

        /// <summary>验证码 TTL（分钟）。</summary>
        public int CodeTtlMinutes { get; set; } = 5;

        /// <summary>验证码位数。</summary>
        public int CodeLength { get; set; } = 6;

        /// <summary>生产模式 fail-fast 门（生产缺 ISmsSender → 拒绝发送）。</summary>
        public bool IsProduction { get; set; }

        /// <summary>场景注册表——scene → 策略（门禁开关 + 未来 per-scene 覆盖）。</summary>
        public Dictionary<string, SmsScenePolicyOptions> Scenes { get; set; } = new()
        {
            [SmsScenes.Login] = new SmsScenePolicyOptions { RequiresCaptcha = true } // M3 接线完成态：login 要求门禁
        };
    }

    /// <summary>场景策略（注册表条目）。</summary>
    public class SmsScenePolicyOptions
    {
        /// <summary>是否要求 CAPTCHA 门禁（M3 门禁内联接线后生效；场景要求门禁但 Captcha 未装配 → 守卫异常 fail-closed，禁静默跳过）。</summary>
        public bool RequiresCaptcha { get; set; }
    }
}