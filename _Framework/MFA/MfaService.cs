using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA 服务门面实现（internal sealed）——绑定管理 + 挑战-验证编排 + 恢复码。
/// <para>数据访问红线（ADR-扩展数据访问红线）：只依赖 SG1 DataService（<see cref="MfaSecretEntityDataService"/>/
/// <see cref="MfaChallengeEntityDataService"/>/<see cref="MfaRecoveryCodeEntityDataService"/>），零 IFreeSql/IEntityDAC
/// 直注入；零 Store 中间层（方案 §四决策7 Oracle C7 具象）。</para>
/// <para>防枚举统一响应（Oracle Q6）：<see cref="VerifyChallengeAsync"/>/<see cref="VerifyRecoveryCodeAsync"/> 失败统一
/// false；未启用用户 <see cref="RequestChallengeAsync"/> 统一"已发起"（随机假 challengeId 验证必败）；
/// 已启用用户 <see cref="EnrollAsync"/> 统一响应（空 EnrollToken）。</para>
/// <para>尝试频控归本服务（per-user×method 内存滑动窗口——Oracle C4：恢复码验证同窗口原语）；发送侧频控
/// （SmsMaxPerHour）归 <see cref="SmsMfaMethod"/>。异常自然传播（唯一约束/业务异常）。</para>
/// </summary>
internal sealed class MfaService : IMfaService
{
    /// <summary>恢复码字母表（Oracle P9：8 位字母数字去易混淆字符 0/O/1/I/l——32 字符幂等无模偏差）。</summary>
    private const string RecoveryCodeCharset = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>恢复码长度（Oracle P9——熵量级对齐 Google/ABP）。</summary>
    private const int RecoveryCodeLength = 8;

    private readonly MfaSecretEntityDataService _secrets;
    private readonly MfaChallengeEntityDataService _challenges;
    private readonly MfaRecoveryCodeEntityDataService _recoveryCodes;
    private readonly IEnumerable<IMfaMethod> _methods;
    private readonly IOptions<MfaOptions> _options;
    private readonly ILogger<MfaService> _logger;

    /// <summary>验证尝试频控器（进程级静态——频控须跨请求生效，不随 scope 重建；单实例语义 Oracle C4/Q3）。</summary>
    private static readonly MfaRateLimiter VerifyLimiter = new();

    public MfaService(
        MfaSecretEntityDataService secrets,
        MfaChallengeEntityDataService challenges,
        MfaRecoveryCodeEntityDataService recoveryCodes,
        IEnumerable<IMfaMethod> methods,
        IOptions<MfaOptions> options,
        ILogger<MfaService> logger)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _challenges = challenges ?? throw new ArgumentNullException(nameof(challenges));
        _recoveryCodes = recoveryCodes ?? throw new ArgumentNullException(nameof(recoveryCodes));
        _methods = methods ?? throw new ArgumentNullException(nameof(methods));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // TOTP secret 密钥惰性初始化（幂等单例）——生产缺 SecretEncryptionKeyPath fail-fast 拒启动（Oracle C6）
        MfaSecretKeyStore.Initialize(options.Value, logger);
    }

    // ── 绑定管理 ──

    /// <inheritdoc />
    public async Task<bool> IsMfaEnabledAsync(string userId, CancellationToken ct = default)
    {
        var secrets = await _secrets.GetByUserAsync(userId, ct);
        return secrets.Any(s => s.IsConfirmed); // 启用 = 任一方法有已激活绑定
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MfaMethodInfo>> GetEnabledMethodsAsync(string userId, CancellationToken ct = default)
    {
        var result = new List<MfaMethodInfo>();
        foreach (var method in _methods)
        {
            var secret = await _secrets.GetActiveSecretAsync(userId, method.Method, ct);
            result.Add(new MfaMethodInfo(method.Method, secret is not null, GetDisplayName(method.Method)));
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<MfaEnrollResult> EnrollAsync(string userId, string method, MfaEnrollContext context, CancellationToken ct = default)
    {
        var m = GetMethod(method);

        // 已启用 → 统一响应（Oracle Q6 防绑定状态探测——不抛，消费方可据此跳绑定 UI）
        if (await _secrets.GetActiveSecretAsync(userId, m.Method, ct) is not null)
            return new MfaEnrollResult(m.Method, EnrollToken: "");

        var request = await m.EnrollAsync(new MfaUser(userId), context ?? new MfaEnrollContext(), ct);
        if (request is null)
            throw new InvalidOperationException($"MFA 方法 {m.Method} 无法发起绑定（上下文不满足——SMS 需提供绑定手机号）");
        return new MfaEnrollResult(m.Method, request.EnrollToken, request.ProvisioningUri);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ConfirmEnrollAsync(string userId, string method, string enrollToken, string code, CancellationToken ct = default)
    {
        var m = GetMethod(method);
        var result = await m.ConfirmEnrollAsync(new MfaUser(userId), enrollToken, code, ct);
        if (!result.Success)
        {
            // 对外统一失败（不区分 EnrollToken 无效/过期/码错——防枚举）；内部日志承载 FailReason
            _logger.LogWarning("MFA 绑定确认失败——UserId={UserId} Method={Method} FailReason={FailReason}",
                userId, m.Method, result.FailReason);
            throw new InvalidOperationException("MFA 绑定确认失败——EnrollToken 无效/过期或验证码错误");
        }

        // 激活成功——首次生成恢复码并一次性明文返回（消费方提示保存；丢失后经 GenerateRecoveryCodesAsync 再生成）
        return await GenerateRecoveryCodesAsync(userId, ct);
    }

    /// <inheritdoc />
    public async Task DisableAsync(string userId, string method, CancellationToken ct = default)
    {
        var m = GetMethod(method);

        // ① 删绑定（含 pending/active 统一物理删除）
        var secrets = await _secrets.GetByUserAsync(userId, ct);
        var secret = secrets.FirstOrDefault(s => s.Method == m.Method);
        if (secret is not null)
            await _secrets.DeleteAsync(secret.Id, ct);

        // ② 删该用户该方法未消费挑战（Oracle Q8-2 级联）
        var challenges = await _challenges.GetActiveByUserMethodAsync(userId, m.Method, ct);
        foreach (var challenge in challenges)
            await _challenges.DeleteAsync(challenge.Id, ct);

        // ③ 删该用户全部恢复码（Oracle Q8-2 级联——任意方法解绑即清空，防跨方法复用）
        await _recoveryCodes.DeleteByUserAsync(userId, ct);
    }

    // ── 挑战-验证 ──

    /// <inheritdoc />
    public async Task<MfaChallengeResult> RequestChallengeAsync(string userId, string method, MfaChallengeContext? context = null, CancellationToken ct = default)
    {
        var m = GetMethod(method);

        // 未启用 → 统一"已发起"（防用户存在性/绑定状态探测；随机假 challengeId 验证必败，Oracle Q6）
        if (await _secrets.GetActiveSecretAsync(userId, m.Method, ct) is null)
            return new MfaChallengeResult(Guid.NewGuid().ToString("N"), m.Method);

        var request = await m.RequestChallengeAsync(new MfaUser(userId), context, ct);
        if (request?.ChallengeId is null)
            throw new InvalidOperationException($"MFA 方法 {m.Method} 无法发起挑战（上下文不满足——SMS 需可发送手机号）");
        return new MfaChallengeResult(request.ChallengeId, m.Method, request.ProvisioningUri);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyChallengeAsync(string userId, string method, string challengeId, string code, CancellationToken ct = default)
    {
        var opt = _options.Value;

        // 先频控（per-user×method 滑动窗口——Oracle C4：成功/失败均计入，每次验证都计；超限抛异常含剩余等待）
        var verifyKey = "mfa:verify:" + userId + ":" + method;
        var verifyWindow = TimeSpan.FromMinutes(opt.VerifyAttemptWindowMinutes);
        if (!VerifyLimiter.IsAllowed(verifyKey, opt.MaxVerifyAttemptsPerWindow, verifyWindow, out _))
        {
            var retryAfter = VerifyLimiter.GetRetryAfter(verifyKey, verifyWindow);
            throw new InvalidOperationException($"MFA 验证尝试过于频繁——请 {Math.Max(1, (int)retryAfter.TotalSeconds)} 秒后重试");
        }

        // 找方法 → 方法验证（失败统一 false 不抛——防枚举；FailReason 内部日志承载）
        var m = GetMethod(method);
        var result = await m.VerifyChallengeAsync(new MfaUser(userId), challengeId, code, ct);
        if (!result.Success)
            _logger.LogWarning("MFA 挑战验证失败（统一 false 防枚举）——UserId={UserId} Method={Method} FailReason={FailReason}",
                userId, m.Method, result.FailReason);
        return result.Success;
    }

    // ── 恢复码 ──

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken ct = default)
    {
        var count = _options.Value.RecoveryCodeCount > 0 ? _options.Value.RecoveryCodeCount : 8;
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
            codes.Add(GenerateRecoveryCode());

        // 全量替换——删旧（含已消费）插新；明文不落库（仅 CodeHash=SHA256 hex 落库，Oracle C1）
        var now = DateTime.UtcNow;
        await _recoveryCodes.DeleteByUserAsync(userId, ct);
        foreach (var code in codes)
        {
            await _recoveryCodes.CreateAsync(new MfaRecoveryCodeEntity
            {
                UserId = userId,
                CodeHash = Sha256Hex(code),
                CreateTime = now,
            }, ct);
        }

        _logger.LogInformation("MFA 恢复码已生成（全量替换）——UserId={UserId} Count={Count}", userId, count);
        return codes;
    }

    /// <inheritdoc />
    public async Task<bool> VerifyRecoveryCodeAsync(string userId, string code, CancellationToken ct = default)
    {
        var opt = _options.Value;

        // 恢复码验证纳入同一频控原语（per-(UserId,"recovery")——Oracle C4：8 码 × 无限尝试可枚举，必须限流）
        var verifyKey = "mfa:verify:" + userId + ":recovery";
        var verifyWindow = TimeSpan.FromMinutes(opt.VerifyAttemptWindowMinutes);
        if (!VerifyLimiter.IsAllowed(verifyKey, opt.MaxVerifyAttemptsPerWindow, verifyWindow, out _))
        {
            var retryAfter = VerifyLimiter.GetRetryAfter(verifyKey, verifyWindow);
            throw new InvalidOperationException($"恢复码验证尝试过于频繁——请 {Math.Max(1, (int)retryAfter.TotalSeconds)} 秒后重试");
        }

        var codeHash = Sha256Hex(code);
        var record = await _recoveryCodes.GetByCodeHashAsync(userId, codeHash, ct);
        if (record is null)
        {
            // 统一失败防枚举（码不存在/已消费/无效均 false）
            _logger.LogWarning("恢复码验证失败（统一 false 防枚举）——UserId={UserId}", userId);
            return false;
        }

        // 原子单次消费（WHERE IsConsumed=false 守卫——并发同码验证恰一成功，Oracle C1；败者 0 行 → 统一 false 防枚举 Q6）
        var affected = await _recoveryCodes.MarkConsumedIfActiveAsync(record.Id, ct);
        if (affected != 1)
        {
            _logger.LogWarning("恢复码并发消费竞态——已由其他请求消费 UserId={UserId}", userId);
            return false;
        }
        return true;
    }

    // ── 私有助手 ──

    /// <summary>按方法标识定位 IMfaMethod（TryAddEnumerable 多注册；未注册 → ArgumentException）。</summary>
    private IMfaMethod GetMethod(string method)
        => _methods.FirstOrDefault(m => string.Equals(m.Method, method, StringComparison.OrdinalIgnoreCase))
           ?? throw new ArgumentException($"未注册的 MFA 方法：{method}", nameof(method));

    /// <summary>方法显示名（前端展示）。</summary>
    private static string GetDisplayName(string method)
        => method switch
        {
            TotpMfaMethod.MethodName => "TOTP 验证器",
            SmsMfaMethod.MethodName => "短信验证码",
            _ => method,
        };

    /// <summary>生成 8 位恢复码（加密安全随机逐位采 32 字符字母表——2^5 无模偏差）。</summary>
    private static string GenerateRecoveryCode()
    {
        var chars = new char[RecoveryCodeLength];
        for (var i = 0; i < RecoveryCodeLength; i++)
            chars[i] = RecoveryCodeCharset[RandomNumberGenerator.GetInt32(RecoveryCodeCharset.Length)];
        return new string(chars);
    }

    /// <summary>SHA256 hex（小写）——恢复码/令牌落库单向散列。</summary>
    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}