using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA 服务门面（public 契约）——绑定管理 + 挑战-验证编排 + 恢复码。
/// <para>职责边界（ADR-MFA-独立扩展与零依赖边界）：MFA = 第二因素验证服务；<b>不签发令牌、不维护登录状态、
/// 不做主认证</b>——登录编排归消费方（主认证 → <see cref="RequestChallengeAsync"/> → <see cref="VerifyChallengeAsync"/>
/// → 消费方签发令牌）。</para>
/// <para>防枚举（Oracle Q6）：<see cref="VerifyChallengeAsync"/> 失败统一 false（不区分挑战不存在/码错/过期）；
/// <see cref="RequestChallengeAsync"/> 对未启用用户返回统一"已发起"；<see cref="EnrollAsync"/> 对已启用用户返回统一响应。</para>
/// </summary>
public interface IMfaService
{
    // ── 绑定管理 ──

    /// <summary>用户是否已启用 MFA（任一方法已激活）。</summary>
    Task<bool> IsMfaEnabledAsync(string userId, CancellationToken ct = default);

    /// <summary>用户已启用的方法列表（含未启用方法信息——供前端展示可绑定方法）。</summary>
    Task<IReadOnlyList<MfaMethodInfo>> GetEnabledMethodsAsync(string userId, CancellationToken ct = default);

    /// <summary>发起绑定——TOTP：生成 secret（返回 ProvisioningUri 供 QR）；SMS：存绑手机。返回 EnrollToken（待激活句柄）。</summary>
    Task<MfaEnrollResult> EnrollAsync(string userId, string method, MfaEnrollContext context, CancellationToken ct = default);

    /// <summary>确认绑定——校验 EnrollToken（单次消费 + TTL）+ 一次码（TOTP 时窗 / SMS 发码）→ 激活（IsConfirmed=true）。
    /// 激活成功**首次生成恢复码**并一次性明文返回（消费方提示用户保存；丢失后经 <see cref="GenerateRecoveryCodesAsync"/> 再生成）。</summary>
    Task<IReadOnlyList<string>> ConfirmEnrollAsync(string userId, string method, string enrollToken, string code, CancellationToken ct = default);

    /// <summary>解绑——删绑定记录 + 未消费挑战 + 恢复码（Oracle Q8-2 级联）。</summary>
    Task DisableAsync(string userId, string method, CancellationToken ct = default);

    // ── 挑战-验证 ──

    /// <summary>发起挑战——TOTP：建票据句柄（无码落库）；SMS：生成码 SHA256 落库 + 经 <see cref="IMfaSmsSender"/> 发送。</summary>
    Task<MfaChallengeResult> RequestChallengeAsync(string userId, string method, MfaChallengeContext? context = null, CancellationToken ct = default);

    /// <summary>验证挑战——TOTP：RFC 6238 时窗校验（±1 容差）；SMS：比对落库码（单次消费）。失败统一 false（防枚举）。</summary>
    Task<bool> VerifyChallengeAsync(string userId, string method, string challengeId, string code, CancellationToken ct = default);

    // ── 恢复码 ──

    /// <summary>生成恢复码（全量替换旧码；返回明文一次性——消费方提示保存）。</summary>
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken ct = default);

    /// <summary>验证恢复码（SHA256 比对 + 单次消费；纳入 per-user 频控——Oracle C4）。</summary>
    Task<bool> VerifyRecoveryCodeAsync(string userId, string code, CancellationToken ct = default);
}

/// <summary>方法启用信息。</summary>
public sealed record MfaMethodInfo(string Method, bool IsEnabled, string? DisplayName = null);

/// <summary>挑战结果——消费方编排句柄。</summary>
public sealed record MfaChallengeResult(string ChallengeId, string Method, string? ProvisioningUri = null);

/// <summary>绑定发起结果——EnrollToken 待确认；TOTP 含 ProvisioningUri。</summary>
public sealed record MfaEnrollResult(string Method, string EnrollToken, string? ProvisioningUri = null, IReadOnlyList<string>? RecoveryCodes = null);

/// <summary>绑定上下文——TOTP：Issuer/DisplayName 进 provisioning URI；SMS：Phone 绑定。</summary>
public sealed record MfaEnrollContext(string? Phone = null, string? Issuer = null, string? DisplayName = null);

/// <summary>挑战上下文——SMS 可覆盖发送手机号。</summary>
public sealed record MfaChallengeContext(string? Phone = null, string? DeviceInfo = null);
