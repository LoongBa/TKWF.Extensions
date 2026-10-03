using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA 方法策略契约——TOTP/SMS 双实现（TryAddEnumerable 多注册，消费方可自定义扩展新方法）。
/// <para>统一契约（ADR-MFA-挑战票据与验证模型）：<see cref="RequestChallengeAsync"/> → <see cref="VerifyChallengeAsync"/>
/// ——TOTP 无状态验证（challengeId 为流程句柄）、SMS 落库验证（challengeId 定位码）；<see cref="EnrollAsync"/> →
/// <see cref="ConfirmEnrollAsync"/> 绑定待激活。</para>
/// <para>⚠️ 多实现必须 <c>TryAddEnumerable</c> 注册（TryAddScoped 同 ServiceType 仅注册首个——Oracle C5）。</para>
/// </summary>
public interface IMfaMethod : IDomainService
{
    /// <summary>方法标识（"totp" / "sms"——服务层按名分派）。</summary>
    string Method { get; }

    /// <summary>发起挑战——返回挑战句柄（null = 方法不支持当前上下文）。</summary>
    Task<MfaChallengeRequest?> RequestChallengeAsync(MfaUser user, MfaChallengeContext? context, CancellationToken ct);

    /// <summary>验证挑战——返回结果（Success + 剩余尝试 + 失败原因——FailReason 内部日志用，对外统一 false）。</summary>
    Task<MfaVerifyResult> VerifyChallengeAsync(MfaUser user, string? challengeId, string code, CancellationToken ct);

    /// <summary>发起绑定——返回 EnrollToken（null = 方法不支持当前上下文）。</summary>
    Task<MfaEnrollRequest?> EnrollAsync(MfaUser user, MfaEnrollContext context, CancellationToken ct);

    /// <summary>确认绑定——校验 EnrollToken + 一次码。</summary>
    Task<MfaVerifyResult> ConfirmEnrollAsync(MfaUser user, string enrollToken, string code, CancellationToken ct);
}

/// <summary>方法上下文载体（UserId + 服务解析 enabled 状态）。</summary>
public sealed record MfaUser(string UserId);

/// <summary>方法内部挑战请求结果。</summary>
public sealed record MfaChallengeRequest(string? ChallengeId = null, string? ProvisioningUri = null);

/// <summary>方法内部绑定请求结果。</summary>
public sealed record MfaEnrollRequest(string EnrollToken, string? ProvisioningUri = null);

/// <summary>验证结果——Success + 剩余尝试（频控提示）+ FailReason（内部日志，对外统一 false）。</summary>
public sealed record MfaVerifyResult(bool Success, int RemainingAttempts, string? FailReason = null);
