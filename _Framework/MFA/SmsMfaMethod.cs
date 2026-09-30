using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.MFA;

/// <summary>
/// SMS MFA 方法——绑手机 + 验证码挑战（独立实现，零扩展间依赖——ADR-MFA-独立扩展与零依赖边界）。
/// <para>语义：6 位码（加密安全随机）+ SHA256 落挑战行（明文不落库）+ TTL 5min + 单次消费（<c>MarkConsumedAsync</c>）+
/// 恒定时间比对；与 Authentication <c>SmsVerificationService</c> 逻辑重叠但<b>语义不同</b>（MFA 第二因素
/// per-user×method vs 首因素 per-phone×scene——Oracle Q1 裁决独立最小化正确）。</para>
/// <para>短信渠道：<see cref="IMfaSmsSender"/> 经 <see cref="IServiceProvider"/> 惰性解析（<c>GetService</c>——未装配
/// 返回 null，防 DI 硬失败）；发送时未装配 → <see cref="MfaMockForbiddenException"/>（503 语义 fail-fast）。</para>
/// <para>发送侧频控（本方法内）：①per-user 小时窗口（<see cref="MfaOptions.SmsMaxPerHour"/>，key <c>mfa:sms:{userId}</c>）
/// ②TTL 内重发拒绝（活动挑战未过期）；验证尝试频控归 <see cref="MfaService"/>（per-user×method 窗口）。</para>
/// </summary>
internal sealed class SmsMfaMethod : IMfaMethod
{
    /// <summary>方法标识（"sms"）。</summary>
    public const string MethodName = "sms";

    private readonly MfaSecretEntityDataService _secrets;
    private readonly MfaChallengeEntityDataService _challenges;
    private readonly IOptions<MfaOptions> _options;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>发送频控器（进程级静态——频控须跨请求生效，不随 scope 重建；单实例语义 Oracle C8）。</summary>
    private static readonly MfaRateLimiter SendLimiter = new();

    public SmsMfaMethod(
        MfaSecretEntityDataService secrets,
        MfaChallengeEntityDataService challenges,
        IOptions<MfaOptions> options,
        IServiceProvider serviceProvider)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _challenges = challenges ?? throw new ArgumentNullException(nameof(challenges));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public string Method => MethodName;

    /// <inheritdoc />
    public async Task<MfaEnrollRequest?> EnrollAsync(MfaUser user, MfaEnrollContext context, CancellationToken ct)
    {
        // 绑定手机必填（SMS 无手机无法发码确认——返回 null，由服务层统一抛"无法发起绑定"）
        if (context is null || string.IsNullOrWhiteSpace(context.Phone))
            return null;
        var phone = context.Phone.Trim();

        var opt = _options.Value;
        var now = DateTime.UtcNow;
        var enrollToken = RandomToken(32);

        // 待激活绑定（Oracle C3）——Phone + IsConfirmed=false + EnrollTokenHash + EnrollExpireAt
        var entity = new MfaSecretEntity
        {
            UserId = user.UserId,
            Method = MethodName,
            Phone = phone,
            IsConfirmed = false,
            EnrollTokenHash = Sha256Hex(enrollToken),
            EnrollExpireAt = now.AddSeconds(opt.ChallengeTtlSeconds),
            CreateTime = now,
            UpdateTime = now,
        };
        await _secrets.CreateAsync(entity, ct);

        // 确认绑定码：生成 6 位码 → 落挑战行 → 经 IMfaSmsSender 发送（发送失败清理挑战行 + 异常自然传播——
        // pending 行滞留，消费方可 Disable 后重绑；频控计数已含本次发送）
        await SendCodeAsync(user.UserId, phone, ct);

        return new MfaEnrollRequest(enrollToken);
    }

    /// <inheritdoc />
    public async Task<MfaVerifyResult> ConfirmEnrollAsync(MfaUser user, string enrollToken, string code, CancellationToken ct)
    {
        var pending = await _secrets.GetPendingSecretAsync(user.UserId, MethodName, ct);
        if (pending is null)
            return new MfaVerifyResult(false, 0, "待激活绑定不存在（已激活/未发起绑定）");

        // EnrollToken 校验——SHA256 恒定时间 + 未过期（Oracle C3：单次消费 + TTL；激活后清空 EnrollTokenHash）
        var now = DateTime.UtcNow;
        var tokenHash = Sha256Hex(enrollToken);
        if (pending.EnrollTokenHash is null
            || pending.EnrollExpireAt is null
            || pending.EnrollExpireAt < now
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(tokenHash), Encoding.UTF8.GetBytes(pending.EnrollTokenHash)))
            return new MfaVerifyResult(false, 0, "EnrollToken 无效或已过期");

        // 取最新活动挑战码比对（SHA256 恒定时间——统一失败不区分原因）
        var challenges = await _challenges.GetActiveByUserMethodAsync(user.UserId, MethodName, ct);
        var challenge = challenges.FirstOrDefault();
        if (challenge?.CodeHash is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sha256Hex(code)), Encoding.UTF8.GetBytes(challenge.CodeHash)))
            return new MfaVerifyResult(false, 0, "短信验证码不匹配（错误/过期/未发送）");

        // 激活——翻转 IsConfirmed + 清空 EnrollTokenHash/ExpireAt + UpdateTime；挑战单次消费（防确认码重放）
        pending.IsConfirmed = true;
        pending.EnrollTokenHash = null;
        pending.EnrollExpireAt = null;
        pending.UpdateTime = now;
        await _secrets.UpdateAsync(pending, ct);

        // 确认码原子单次消费（WHERE IsConsumed=false 守卫——并发 ConfirmEnroll 恰一成功，Oracle C1）
        var affected = await _challenges.MarkConsumedIfActiveAsync(challenge.Id, DateTime.UtcNow, ct);
        if (affected != 1)
            return new MfaVerifyResult(false, 0, "ChallengeAlreadyConsumed");

        return new MfaVerifyResult(true, 0);
    }

    /// <inheritdoc />
    public async Task<MfaChallengeRequest?> RequestChallengeAsync(MfaUser user, MfaChallengeContext? context, CancellationToken ct)
    {
        var active = await _secrets.GetActiveSecretAsync(user.UserId, MethodName, ct);
        if (active is null)
            return null; // 未启用——服务层统一"已发起"防枚举（Oracle Q6）

        // 发送手机 = context.Phone 覆盖 ?? 绑定 Phone（MfaChallengeContext.Phone 支持重发场景指定）
        var phone = context?.Phone;
        var targetPhone = string.IsNullOrWhiteSpace(phone) ? active.Phone : phone.Trim();
        if (string.IsNullOrWhiteSpace(targetPhone))
            return null; // 无可用手机（激活绑定应有 Phone，防御）

        var challenge = await SendCodeAsync(user.UserId, targetPhone, ct);
        return new MfaChallengeRequest(challenge.Id.ToString());
    }

    /// <inheritdoc />
    public async Task<MfaVerifyResult> VerifyChallengeAsync(MfaUser user, string? challengeId, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || !long.TryParse(challengeId, out var id))
            return new MfaVerifyResult(false, 0, "挑战句柄非法（服务层统一假句柄验证必败）");

        var challenge = await _challenges.GetActiveByIdAsync(id, ct);
        if (challenge is null || challenge.UserId != user.UserId || challenge.Method != MethodName)
            return new MfaVerifyResult(false, 0, "挑战不存在/已消费/已过期");

        // 比对落库码（SHA256 恒定时间——统一失败不区分原因）
        if (challenge.CodeHash is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sha256Hex(code)), Encoding.UTF8.GetBytes(challenge.CodeHash)))
            return new MfaVerifyResult(false, 0, "短信验证码不匹配");

        // 成功——原子单次消费（WHERE IsConsumed=false 守卫——并发验证恰一成功，Oracle C1；败者 0 行 → 统一失败）
        var affected = await _challenges.MarkConsumedIfActiveAsync(challenge.Id, DateTime.UtcNow, ct);
        if (affected != 1)
            return new MfaVerifyResult(false, 0, "ChallengeAlreadyConsumed");
        return new MfaVerifyResult(true, 0);
    }

    // ── 发送私有路径 ──

    /// <summary>生成 6 位码 → 落挑战行（SHA256 明文不落库）→ 经 <see cref="IMfaSmsSender"/> 发送；返回创建的挑战（challengeId 句柄）。
    /// <para>发送侧双频控：①TTL 内重发拒绝（活动挑战未过期）②per-user 小时窗口（SmsMaxPerHour，Oracle C8 单实例语义）；
    /// 渠道未装配 → <see cref="MfaMockForbiddenException"/>（503 语义 fail-fast）；发送失败 → 清理孤儿挑战行
    /// （码未发出挑战无意义——防阻塞重发至 TTL，Oracle7 C1）并异常自然传播。</para></summary>
    private async Task<MfaChallengeEntity> SendCodeAsync(string userId, string phone, CancellationToken ct)
    {
        var opt = _options.Value;
        var now = DateTime.UtcNow;

        // ① TTL 内重发拒绝——用户该方法存在活动（未消费 + 未过期）挑战 → 拒绝重发（对齐方案"沿用挑战 TTL 5min"）
        var activeChallenges = await _challenges.GetActiveByUserMethodAsync(userId, MethodName, ct);
        if (activeChallenges.Count > 0)
            throw new InvalidOperationException("短信验证码已发送——请等待当前验证码过期或消费后重试");

        // ② per-user 小时窗口（内存滑动窗口；⚠️ 多实例不跨实例——Oracle C8 单实例部署或外部限流器前置）
        var limiterKey = "mfa:sms:" + userId;
        var hourWindow = TimeSpan.FromHours(1);
        if (!SendLimiter.IsAllowed(limiterKey, opt.SmsMaxPerHour, hourWindow, out _))
        {
            var retryAfter = SendLimiter.GetRetryAfter(limiterKey, hourWindow);
            throw new InvalidOperationException($"短信发送过于频繁——请 {Math.Max(1, (int)retryAfter.TotalSeconds)} 秒后重试");
        }

        // 落挑战行（码 SHA256——明文不落库；TTL 对齐 ChallengeTtlSeconds）
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
        var challenge = new MfaChallengeEntity
        {
            UserId = userId,
            Method = MethodName,
            CodeHash = Sha256Hex(code),
            ExpireAt = now.AddSeconds(opt.ChallengeTtlSeconds),
            CreateTime = now,
        };
        var created = await _challenges.CreateAsync(challenge, ct);

        // 经 IMfaSmsSender 发送——发送失败清理孤儿挑战行（码未发出，挑战无意义；阻塞重发至 TTL 属体验缺陷，Oracle7 C1）
        try
        {
            var sender = _serviceProvider.GetService<IMfaSmsSender>();
            if (sender is null)
                throw new MfaMockForbiddenException("IMfaSmsSender 未装配——MFA SMS 方法必须由消费方实现短信发送渠道（生产 503 语义 fail-fast）");
            await sender.SendAsync(
                new MfaSmsMessage(phone, $"您的 MFA 验证码是 {code}，{opt.ChallengeTtlSeconds / 60} 分钟内有效（请勿转发他人）"),
                ct);
        }
        catch
        {
            // 发送失败——清理孤儿挑战行（码未发出，挑战无意义；阻塞重发至 TTL 属体验缺陷，Oracle7 C1）
            await _challenges.DeleteAsync(created.Id, ct);
            throw; // 异常自然传播（消费方可感知失败并重试）
        }
        return created;
    }

    /// <summary>SHA256 hex（小写）——码/令牌落库单向散列。</summary>
    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>加密安全随机 hex（小写）——EnrollToken 明文（SHA256 落库）。</summary>
    private static string RandomToken(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}