using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.MFA;

/// <summary>
/// TOTP MFA 方法（RFC 6238 自研，零第三方）——验证器绑定 + 无状态挑战验证。
/// <para>绑定：<see cref="EnrollAsync"/> 生成 Base32 secret → AES-GCM 密文落绑定行（明文不落库——
/// <see cref="MfaSecretKeyStore"/>，ADR-MFA-TOTP自研与密钥存储）；<see cref="ConfirmEnrollAsync"/> 校验
/// EnrollToken（SHA256 恒定时间 + TTL，Oracle C3）+ 一次码（时窗）→ 激活（IsConfirmed=true + 清空
/// EnrollTokenHash/EnrollExpireAt）。</para>
/// <para>挑战：<see cref="RequestChallengeAsync"/> 建票据句柄（CodeHash=null——TOTP 无码落库，仅流程句柄 +
/// 频控挂点 + 审计，ADR-MFA-挑战票据与验证模型 Q5）；<see cref="VerifyChallengeAsync"/> 按 secret + RFC 6238
/// 时窗（±<see cref="MfaOptions.TotpClockSkewWindows"/>）验码 + 消费票据（单次防重放，Oracle C1）。</para>
/// <para>⚠️ 依赖清单含 <see cref="MfaChallengeEntityDataService"/>（任务说明仅列 secret+options——挑战票据建行/消费
/// 必须经挑战 DataService，为任务方法规范的权威要求）。</para>
/// <para>V4.10.55（ADR92，T3 闭环）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>TryAddEnumerableConstructible&lt;IMfaMethod, TotpMfaMethod&gt;</c>（集合版守卫工厂——
/// 帧内经 CurrentAopUser 供给 ctor 的 IDomainUser；帧外枚举抛守卫）。<c>[DiContractIgnore]</c>：豁免 DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class TotpMfaMethod : DomainServiceBase, IMfaMethod
{
    /// <summary>方法标识（"totp"）。</summary>
    public const string MethodName = "totp";

    private MfaSecretEntityDataService? _secrets;
    private MfaChallengeEntityDataService? _challenges;
    private readonly IOptions<MfaOptions> _options;

    private MfaSecretEntityDataService Secrets => _secrets ??= User.Use<MfaSecretEntityDataService>();
    private MfaChallengeEntityDataService Challenges => _challenges ??= User.Use<MfaChallengeEntityDataService>();

    public TotpMfaMethod(
        IDomainUser user,
        IOptions<MfaOptions> options)
        : base(user)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string Method => MethodName;

    /// <inheritdoc />
    public async Task<MfaEnrollRequest?> EnrollAsync(MfaUser user, MfaEnrollContext context, CancellationToken ct)
    {
        var opt = _options.Value;
        var now = DateTime.UtcNow;

        // 生成 secret → AES-GCM 加密（密钥经 MfaSecretKeyStore——MfaService 构造时幂等 Initialize，缺密钥生产 fail-fast）
        var base32Secret = TotpGenerator.GenerateSecret();
        var secretEncrypted = MfaSecretKeyStore.Encrypt(base32Secret, MfaSecretKeyStore.GetKey());

        // 待激活绑定（Oracle C3）——IsConfirmed=false + EnrollTokenHash（SHA256 落库）+ EnrollExpireAt（TTL 对齐 ChallengeTtl）
        var enrollToken = RandomToken(32);
        var entity = new MfaSecretEntity
        {
            UserId = user.UserId,
            Method = MethodName,
            SecretEncrypted = secretEncrypted,
            IsConfirmed = false,
            EnrollTokenHash = Sha256Hex(enrollToken),
            EnrollExpireAt = now.AddSeconds(opt.ChallengeTtlSeconds),
            CreateTime = now,
            UpdateTime = now,
        };
        await Secrets.CreateAsync(entity, ct);

        // provisioning URI——issuer/displayName 取自绑定上下文（缺省 TKWF/用户 Id，供前端 QR）
        var issuer = string.IsNullOrWhiteSpace(context.Issuer) ? "TKWF" : context.Issuer!.Trim();
        var accountName = string.IsNullOrWhiteSpace(context.DisplayName) ? user.UserId : context.DisplayName!.Trim();
        var provisioningUri = TotpGenerator.BuildProvisioningUri(issuer, accountName, base32Secret, opt.TotpDigits, opt.TotpTimeStepSeconds);

        return new MfaEnrollRequest(enrollToken, provisioningUri);
    }

    /// <inheritdoc />
    public async Task<MfaVerifyResult> ConfirmEnrollAsync(MfaUser user, string enrollToken, string code, CancellationToken ct)
    {
        var opt = _options.Value;
        var now = DateTime.UtcNow;

        var pending = await Secrets.GetPendingSecretAsync(user.UserId, MethodName, ct);
        if (pending is null)
            return new MfaVerifyResult(false, 0, "待激活绑定不存在（已激活/未发起绑定）");

        // EnrollToken 校验——SHA256 恒定时间 + 未过期（Oracle C3：单次消费（激活后清空）+ TTL；不区分原因统一失败）
        var tokenHash = Sha256Hex(enrollToken);
        if (pending.EnrollTokenHash is null
            || pending.EnrollExpireAt is null
            || pending.EnrollExpireAt < now
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(tokenHash), Encoding.UTF8.GetBytes(pending.EnrollTokenHash)))
            return new MfaVerifyResult(false, 0, "EnrollToken 无效或已过期");

        // 解密 secret → 一次码时窗校验（RFC 6238——激活确认输一次验证器码）
        if (pending.SecretEncrypted is null)
            return new MfaVerifyResult(false, 0, "绑定缺少 secret（数据异常）");
        var base32Secret = MfaSecretKeyStore.Decrypt(pending.SecretEncrypted, MfaSecretKeyStore.GetKey());
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!TotpGenerator.Validate(code, base32Secret, unixSeconds, opt.TotpTimeStepSeconds, opt.TotpDigits, opt.TotpClockSkewWindows))
            return new MfaVerifyResult(false, 0, "TOTP 验证码校验失败（码错/过期——统一失败不区分原因）");

        // 激活——翻转 IsConfirmed + 清空 EnrollTokenHash/EnrollExpireAt + UpdateTime（SecretEncrypted CanUpdate=false 不可改）
        pending.IsConfirmed = true;
        pending.EnrollTokenHash = null;
        pending.EnrollExpireAt = null;
        pending.UpdateTime = now;
        await Secrets.UpdateAsync(pending, ct);

        return new MfaVerifyResult(true, 0);
    }

    /// <inheritdoc />
    public async Task<MfaChallengeRequest?> RequestChallengeAsync(MfaUser user, MfaChallengeContext? context, CancellationToken ct)
    {
        var active = await Secrets.GetActiveSecretAsync(user.UserId, MethodName, ct);
        if (active is null)
            return null; // 未启用——服务层统一"已发起"防枚举（Oracle Q6）

        // TOTP 无状态挑战——票据仅作流程句柄（CodeHash null）+ 频控挂点 + 审计（ADR 挑战票据模型 Q5）
        var now = DateTime.UtcNow;
        var challenge = new MfaChallengeEntity
        {
            UserId = user.UserId,
            Method = MethodName,
            CodeHash = null,
            ExpireAt = now.AddSeconds(_options.Value.ChallengeTtlSeconds),
            CreateTime = now,
        };
        var created = await Challenges.CreateAsync(challenge, ct);
        return new MfaChallengeRequest(created.Id.ToString());
    }

    /// <inheritdoc />
    public async Task<MfaVerifyResult> VerifyChallengeAsync(MfaUser user, string? challengeId, string code, CancellationToken ct)
    {
        var opt = _options.Value;
        if (string.IsNullOrWhiteSpace(challengeId) || !long.TryParse(challengeId, out var id))
            return new MfaVerifyResult(false, 0, "挑战句柄非法（服务层统一假句柄验证必败）");

        var active = await Secrets.GetActiveSecretAsync(user.UserId, MethodName, ct);
        if (active?.SecretEncrypted is null)
            return new MfaVerifyResult(false, 0, "TOTP 未启用或 secret 缺失");

        // 票据归属校验（活动 = 未消费 + 未过期；须属当前用户/方法——统一失败防枚举）
        var challenge = await Challenges.GetActiveByIdAsync(id, ct);
        if (challenge is null || challenge.UserId != user.UserId || challenge.Method != MethodName)
            return new MfaVerifyResult(false, 0, "挑战不存在/已消费/已过期");

        // 解密 secret → 时窗验证（TOTP 无状态语义——验证不依赖票据码）
        var base32Secret = MfaSecretKeyStore.Decrypt(active.SecretEncrypted, MfaSecretKeyStore.GetKey());
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!TotpGenerator.Validate(code, base32Secret, unixSeconds, opt.TotpTimeStepSeconds, opt.TotpDigits, opt.TotpClockSkewWindows))
            return new MfaVerifyResult(false, 0, "TOTP 验证码校验失败（码错/过期——统一失败不区分原因）");

        // 成功——原子单次消费（WHERE IsConsumed=false 守卫——并发验证恰一成功，Oracle C1；败者 0 行 → 统一失败）
        var affected = await Challenges.MarkConsumedIfActiveAsync(challenge.Id, DateTime.UtcNow, ct);
        if (affected != 1)
            return new MfaVerifyResult(false, 0, "ChallengeAlreadyConsumed");
        return new MfaVerifyResult(true, 0);
    }

    /// <summary>SHA256 hex（小写）——EnrollTokenHash 落库单向散列。</summary>
    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>加密安全随机 hex（小写）——EnrollToken 明文（SHA256 落库）。</summary>
    private static string RandomToken(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}