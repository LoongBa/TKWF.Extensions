using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.Authentication;

/// <summary>短信消息（方案 §5.5——对齐 Emailing IEmailSender 模式，装配层实现渠道）。</summary>
/// <param name="Phone">目标手机号。</param>
/// <param name="Content">短信内容（验证码文本由装配层/服务商模板组装）。</param>
public sealed record SmsMessage(string Phone, string Content);

/// <summary>
/// 短信发送器契约——装配层实现（腾讯云/阿里云等渠道）。
/// <para>方案 §5.5——Ext 零第三方依赖；参考 Emailing <c>IEmailSender</c> 先例（TryAdd 语义消费方实现优先）；</para>
/// <para>生产未实现 ISmsSender 时 SmsVerificationService 抛 <see cref="SmsMockForbiddenException"/>（503 语义防 dev_code 泄露）。</para>
/// </summary>
public interface ISmsSender
{
    /// <summary>发送短信（失败抛异常——由调用方记录 SmsRecord 失败）。</summary>
    Task SendAsync(SmsMessage message, CancellationToken ct = default);
}

/// <summary>短信场景常量（方案 §5.3——SmsRecord.Scene 列）。</summary>
public static class SmsScenes
{
    /// <summary>登录。</summary>
    public const string Login = "login";

    /// <summary>注册。</summary>
    public const string Register = "register";

    /// <summary>绑定。</summary>
    public const string Bind = "bind";

    /// <summary>重置。</summary>
    public const string Reset = "reset";
}

/// <summary>
/// 短信验证码服务契约——发送（生成 6 位码 + TTL 5min + 频控 60s/小时/天/IP）+ 校验（单次消费 + 频控 5 次/小时）。
/// <para>方案 §5.5——验证码单向散列（SHA256）落库（SmsRecordEntity）；生产 Mock → 503 语义（SmsMockForbiddenException）。</para>
/// </summary>
public interface ISmsVerificationService : IDomainService
{
    /// <summary>发送验证码（频控命中抛 AuthenticationException；生产未接 ISmsSender 抛 SmsMockForbiddenException）。</summary>
    [AllowAnonymousFlag]
    Task SendCodeAsync(string phone, string scene, CancellationToken ct = default);

    /// <summary>校验验证码（成功单次消费；过期/不匹配/频控 → false 或抛 AuthenticationException）。</summary>
    [AllowAnonymousFlag]
    Task<bool> VerifyCodeAsync(string phone, string code, string scene, CancellationToken ct = default);
}

/// <summary>生产 Mock 语义异常——未装配 ISmsSender 时拒绝发送（503，防 dev_code 泄露，Oracle D7）。</summary>
public class SmsMockForbiddenException : System.Security.Authentication.AuthenticationException
{
    public SmsMockForbiddenException(string message) : base(message) { }
}
