using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 找回密码服务（V0.9.0 B.10/B.11——AuthCenter 自建多通道链路，UId-keyed，**不实现 <c>IAccountPasswordManager</c> 第二实现**）。
/// <para>ADR B.10 边界：<c>IAccountPasswordManager</c>（TKWF.Ext.Account.Abstractions）按 userName 写 IdentityUser，
/// 与 AuthAccount UId/Phone 模型平行不互认——本服务面向 AuthAccount 凭据（UId-keyed + 多通道 + 自带投递）。</para>
/// <para>三通道层级（B.10）：SMS = 通道可达证明（<see cref="SmsScenes.Reset"/> 现成）；Email = 通道可达（<see cref="IEmailSender"/> 可空降级）；
/// 扫码 = 身份持有证明（前置条件，改密前证明持有微信——OAuthTicket 链路设计，通道编排归消费方/装配层）。</para>
/// <para>自带投递（B.11）：InitiateResetAsync 生成码后**自行投递**（SMS 经 <see cref="ISmsVerificationService.SendCodeAsync"/> Reset 场景；
/// Email 经 Emailing 投递；修复 Account <c>DefaultPasswordResetFlow</c> 生成码不投递缺口）。</para>
/// <para>落地：<see cref="SetPasswordAsync"/> 委托 <see cref="IAuthAccountService.SetPasswordAsync"/>（PasswordHasher + TokenVersion++）。</para>
/// </summary>
public interface IPasswordResetService : IDomainService
{
    /// <summary>发起找回（SMS 通道）——发送验证码（SmsScenes.Reset 现成，自带投递；幂等生成 + 落库重置码记录）；
    /// 返回发送结果（已发送 / 通道未配置降级语义码）。</summary>
    Task<string> InitiateSmsResetAsync(string identifier, CancellationToken ct = default);

    /// <summary>发起找回（Email 通道）——查 UserProfile.Email → Emailing 投递重置码（可空依赖降级：
    /// 未接 Emailing → 返回 EMAIL_SENDER_NOT_CONFIGURED；接 → 已发送）。</summary>
    Task<string> InitiateEmailResetAsync(string identifier, CancellationToken ct = default);

    /// <summary>完成找回（SMS/Email 验证码通道）——校验重置码（单次消费防重放）→ 设新密码（SecurePassword：客户端算
    /// <paramref name="newClientHash"/>+<paramref name="newSalt"/>，服务端零明文——ADR-密码策略与口令协议 决策 1）。
    /// 校验失败抛 AuthenticationException（RESET_CODE_INVALID / RESET_CODE_EXPIRED / RESET_CODE_CONSUMED）。</summary>
    Task CompleteResetWithCodeAsync(string identifier, string resetCode, string newClientHash, string newSalt, CancellationToken ct = default);

    /// <summary>完成找回（SMS 通道统一方法——P1-NEW-1 修订）——校验短信验证码（<c>VerifyCodeAsync(phone, code, SmsScenes.Reset)</c>
    /// 单次消费）→ 设新密码（clientHash+salt，零明文）；校验失败抛 AuthenticationException
    /// （SMS_CODE_MISMATCH / SMS_CODE_EXPIRED / SMS_VERIFY_ATTEMPT_LIMIT——SmsVerificationService 语义透传）。</summary>
    Task CompleteResetSmsAsync(string identifier, string phone, string code, string newClientHash, string newSalt, CancellationToken ct = default);

    /// <summary>完成找回（扫码通道前置——已证明身份持有微信）——直接设新密码（clientHash+salt，零明文；OAuthTicket 链路证明归装配层编排）。</summary>
    Task CompleteResetVerifiedAsync(string uid, string newClientHash, string newSalt, CancellationToken ct = default);
}