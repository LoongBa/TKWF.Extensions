using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SmsCode
{
    /// <summary>
    /// 短信消息（对齐 Emailing IEmailSender 模式——装配层实现渠道）。
    /// </summary>
    /// <param name="Phone">目标手机号（v1 = DeliveryTarget）。</param>
    /// <param name="Content">短信内容（验证码文本由扩展组装，服务商模板由装配层处理）。</param>
    public sealed record SmsMessage(string Phone, string Content);

    /// <summary>
    /// 短信发送器契约（send-only——发送即服务形态；生产未装配 → 503 fail，对齐 AuthCenter ISmsSender 先例）。
    /// <para>注册：TryAdd（无默认实现；消费方实现腾讯云/阿里云渠道）；dev 模式缺省 → 日志输出码（dev mock）。</para>
    /// </summary>
    public interface ISmsSender
    {
        /// <summary>发送短信（失败抛异常——由调用方记录失败，记录不标记 verified，自然过期作废）。</summary>
        Task SendAsync(SmsMessage message, CancellationToken ct = default);
    }

    /// <summary>
    /// 短信验证码作用域解析器——租户维度归消费方（oracle3 裁定；X' 冻结项保留 SPI 形状）。
    /// <para>DMP：<c>merchant:{MerchantUId}</c>（Scoped，经 IHttpContextAccessor 读路由值 <c>mid</c>）。
    /// 默认空串实现（单例）供无租户消费方；频控键 = <c>smscode:{scope}:{scene}:{subject}</c>。</para>
    /// <para>返回规则：<c>null</c> = 租户缺失（ScopeMissing fail-closed——DMP mid 缺失场景）；
    /// 空串 = 无租户消费方（合法默认，键 <c>smscode::{scene}:{subject}</c>）。</para>
    /// </summary>
    public interface ISmsCodeScopeResolver
    {
        /// <summary>作用域前缀（DMP: "merchant:{MerchantUId}"；无租户 → 空串；缺失 → null）。</summary>
        string? GetScopePrefix();
    }

    /// <summary>
    /// 短信验证码服务门面——发送（含 CAPTCHA 门禁内联 + 频控 + 单次消费）/ 校验。
    /// <para><b>X' 冻结项 2（错误契约分层）</b>：引擎层返回类型化结果（错误码映射住 DMP 门面/端点层——
    /// MFA 未来委托不继承 DMP 错误契约）；DMP 端点据此映射 snake_case 错误码（§6 冻结表）。</para>
    /// <para><b>Q6 门禁内联</b>：scene.RequiresCaptcha=true → 先验 captchaToken（缺失/无效 → Fail=CaptchaFailed）；
    /// 场景要求门禁但 Captcha 未装配 → 守卫异常（禁静默跳过）。</para>
    /// </summary>
    public interface ISmsCodeService : IDomainService
    {
        /// <summary>发送验证码——门禁序：CAPTCHA 校验 → 频控检查 → 生成码 → 落库 → 发送。</summary>
        /// <param name="scene">场景（SmsScenes 白名单，未知 → UnknownScene）。</param>
        /// <param name="subject">不透明主体键（v1 恒=手机号，X' 冻结项 1）。</param>
        /// <param name="deliveryTarget">投递目标（v1 恒=手机号）。</param>
        /// <param name="captchaToken">CAPTCHA 挑战回答凭据（scene.RequiresCaptcha=true 时必填，M3 接线后生效）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<SmsCodeSendResult> SendCodeAsync(string scene, string subject, string deliveryTarget,
            string? captchaToken, CancellationToken ct = default);

        /// <summary>校验验证码——单次消费 CAS + 恒定时间比对 + 尝试频控 + FailedAttempts 作废。</summary>
        Task<SmsCodeVerifyResult> VerifyCodeAsync(string scene, string subject, string code,
            CancellationToken ct = default);
    }

    /// <summary>发送结果（引擎层类型化——M1 闭环：带 SmsCodeSendFailure 判别，DMP 端点映射无歧义）。</summary>
    /// <param name="Sent">是否已发出。</param>
    /// <param name="Failure">失败判别（Sent=false 时非 null）。</param>
    /// <param name="RetryAfterSeconds">频控命中时的重试等待秒数（距最严约束重置秒数，消费方不得自推）。</param>
    public sealed record SmsCodeSendResult(bool Sent, SmsCodeSendFailure? Failure, int? RetryAfterSeconds);

    /// <summary>发送失败判别——DMP 端点据此映射 chaptcha_failed(400)/rate_limited(429)/sms_sender_unavailable(503) 等。</summary>
    public enum SmsCodeSendFailure
    {
        /// <summary>未失败（Sent=true）。</summary>
        None,

        /// <summary>CAPTCHA 门禁失败（chaptcha_failed 400）。</summary>
        CaptchaFailed,

        /// <summary>频控命中（rate_limited 429，携带 RetryAfterSeconds）。</summary>
        RateLimited,

        /// <summary>发送器不可用（sms_sender_unavailable 503——生产未装配 ISmsSender 或发送异常）。</summary>
        SenderUnavailable,

        /// <summary>未知场景（invalid_scene 400——场景不在注册表，fail-closed，不落入任何频控键）。</summary>
        UnknownScene,

        /// <summary>作用域缺失（invalid_merchant 400——ScopeResolver 未装配/返回空白）。</summary>
        ScopeMissing
    }

    /// <summary>校验结果（引擎层类型化——X' 冻结项 2）。</summary>
    /// <param name="Verified">是否校验通过。</param>
    /// <param name="Failure">失败判别（Verified=false 时非 null）。</param>
    public sealed record SmsCodeVerifyResult(bool Verified, SmsCodeFailure? Failure);

    /// <summary>校验失败判别——DMP 端点映射 invalid_code(400)/code_expired(400)/rate_limited(429)。</summary>
    public enum SmsCodeFailure
    {
        /// <summary>未失败（Verified=true）。</summary>
        None,

        /// <summary>未找到活动验证码（invalid_code 400——未发送/已消费/已作废统一）。</summary>
        NotFound,

        /// <summary>已过期（code_expired 400）。</summary>
        Expired,

        /// <summary>验证码不匹配（invalid_code 400）。</summary>
        Mismatch,

        /// <summary>校验频控命中（rate_limited 429——15min 窗 verify 调用计数）。</summary>
        RateLimited,

        /// <summary>未知场景（invalid_scene 400——fail-closed）。</summary>
        UnknownScene,

        /// <summary>作用域缺失（invalid_merchant 400）。</summary>
        ScopeMissing
    }
}