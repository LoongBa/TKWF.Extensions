using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.Captcha;

namespace TKWF.Ext.SmsCode;

/// <summary>
/// 短信验证码服务实现——发送（门禁序：CAPTCHA校验(M3) → 频控检查 → 生成码 → 落库 → 发送）+ 校验（单次消费 CAS + 恒定时间比对）。
/// <para>数据访问红线合规：仅依赖 <see cref="SmsCodeRecordEntityDataService"/>（SG1 DataService），不注入 IFreeSql/IEntityDAC。</para>
/// <para>正确路线（ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;ISmsCodeService, SmsCodeService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// <para>X' 冻结项落实：Subject 不透明键控（§4.1）；类型化结果 + 错误契约分层（§4.4）；
/// 频控键独立前缀 <c>smscode:</c>（严禁复用 AuthCenter <c>sms-ip:</c> 桶）；未知场景 fail-closed（§4.6 m6 闭环）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class SmsCodeService : DomainServiceBase, ISmsCodeService
{
    private readonly ISmsSender? _sender;
    private readonly ISmsCodeScopeResolver _scopeResolver;
    private readonly IOptions<SmsCodeOptions> _options;
    private readonly ILogger<SmsCodeService> _logger;
    private readonly IRateLimitCheck? _rateLimit;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private SmsCodeRecordEntityDataService? _dataService;

    private SmsCodeRecordEntityDataService DataService => _dataService ??= User.Use<SmsCodeRecordEntityDataService>();

    public SmsCodeService(
        IDomainUser user,
        ISmsSender? sender = null,
        ISmsCodeScopeResolver? scopeResolver = null,
        IOptions<SmsCodeOptions>? options = null,
        ILogger<SmsCodeService>? logger = null,
        IRateLimitCheck? rateLimit = null,
        IHttpContextAccessor? httpContextAccessor = null)
        : base(user)
    {
        _sender = sender;
        _scopeResolver = scopeResolver ?? throw new ArgumentNullException(nameof(scopeResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimit = rateLimit;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public async Task<SmsCodeSendResult> SendCodeAsync(string scene, string subject, string deliveryTarget,
        string? captchaToken, CancellationToken ct = default)
    {
        // (0) 场景校验——未知场景 fail-closed（不落入任何频控键，防换场景绕限）
        if (!_options.Value.Scenes.TryGetValue(scene, out var scenePolicy))
            return new SmsCodeSendResult(false, SmsCodeSendFailure.UnknownScene, null);

        // (0.5) 作用域解析——null = 租户缺失
        var scope = _scopeResolver.GetScopePrefix();
        if (scope is null)
            return new SmsCodeSendResult(false, SmsCodeSendFailure.ScopeMissing, null);

        // (0.75) CAPTCHA 门禁（M3 接线——Q6 门禁序：CAPTCHA 校验 → 频控检查 → 生成码 → 落库 → 发送；
        //        m7 守卫：场景要求门禁但 Captcha 未装配 → 守卫异常（禁静默跳过——防短信预算裸奔））
        if (scenePolicy.RequiresCaptcha)
        {
            // captchaToken 格式冻结："{challengeId}:{answer}"（challengeId=32hex、answer=去混淆字母表均不含 ':'，分隔安全）
            var (challengeId, answer) = SplitCaptchaToken(captchaToken);
            if (challengeId is null)
                return new SmsCodeSendResult(false, SmsCodeSendFailure.CaptchaFailed, null);

            var captchaService = User.Use<ICaptchaService>();   // 未装配 → DomainGuardException（fail-closed，禁静默跳过）
            var captchaResult = await captchaService.VerifyAsync(challengeId, answer!, ct);
            if (!captchaResult.Passed)
                return new SmsCodeSendResult(false, SmsCodeSendFailure.CaptchaFailed, null);
        }

        var now = DateTime.UtcNow;
        var options = _options.Value;

        // (1) 频控检查——重发间隔 / 小时 / 日（SmsCodeRecord 为频控数据源，含已消费与过期记录）
        //     retryAfter 语义（方案 §6）："距最严约束重置秒数"= 窗口内最老记录 + 窗口 - now（审核 P2 修复：
        //     旧实现给"窗长"或"到午夜"——非真实重置时刻，误导消费方自推）
        var latest = await DataService.GetLatestUnverifiedAsync(subject, scene, ct);
        if (latest is not null
            && now.Subtract(latest.CreateTime).TotalSeconds < options.SmsResendIntervalSeconds)
        {
            var retryAfter = options.SmsResendIntervalSeconds - (int)now.Subtract(latest.CreateTime).TotalSeconds;
            return new SmsCodeSendResult(false, SmsCodeSendFailure.RateLimited, retryAfter);
        }

        if (await DataService.CountSentAsync(subject, scene, now.AddHours(-1), ct) >= options.SmsHourlyLimitPerSubject)
        {
            var oldest = await DataService.GetOldestInWindowAsync(subject, scene, now.AddHours(-1), ct);
            var retryAfter = oldest is null ? 3600 : 3600 - (int)now.Subtract(oldest.Value).TotalSeconds;
            return new SmsCodeSendResult(false, SmsCodeSendFailure.RateLimited, retryAfter);
        }

        if (await DataService.CountSentAsync(subject, scene, now.AddDays(-1), ct) >= options.SmsDailyLimitPerSubject)
        {
            var oldest = await DataService.GetOldestInWindowAsync(subject, scene, now.AddDays(-1), ct);
            var retryAfter = oldest is null ? 86400 : 86400 - (int)now.Subtract(oldest.Value).TotalSeconds;
            return new SmsCodeSendResult(false, SmsCodeSendFailure.RateLimited, retryAfter);
        }

        // (1.5) 活跃未消费码上限（防轰炸发送）——超过即频控拒绝
        if (await DataService.CountActiveAsync(subject, scene, ct) >= options.MaxActiveCodesPerSubject)
            return new SmsCodeSendResult(false, SmsCodeSendFailure.RateLimited, options.SmsResendIntervalSeconds);

        // (2) IP 维度频控——send + verify 共享单桶（smscode:ip:{scope}:{ip}，fail-closed unknown 桶）
        var ipAddress = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
        var ipKey = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress;
        if (_rateLimit is not null
            && !_rateLimit.TryAcquire($"smscode:ip:{scope}:{ipKey}", options.SmsHourlyLimitPerIp, TimeSpan.FromHours(1), out _))
            return new SmsCodeSendResult(false, SmsCodeSendFailure.RateLimited, options.SmsResendIntervalSeconds);

        // (3) 生成验证码——加密安全随机（引擎原语）
        var code = VerificationCodeEngine.GenerateCode(options.CodeLength);

        // (4) 重发作废旧码（M3 闭环）——本 (Subject,Scene) 全部未消费未过期记录 ExpireAt 置 now（防活跃计数膨胀）
        await DataService.InvalidateActiveAsync(subject, scene, ct);

        // (5) 落库（CodeHash=SHA256 hex 小写，ExpireAt=now+TTL，IpAddress）
        var record = new SmsCodeRecordEntity
        {
            Subject = subject,
            DeliveryTarget = deliveryTarget,
            Scene = scene,
            CodeHash = VerificationCodeEngine.HashCode(code),
            IsVerified = false,
            IpAddress = ipAddress,
            ExpireAt = now.AddMinutes(options.CodeTtlMinutes),
            CreateTime = now
        };
        await DataService.CreateAsync(record, ct);

        // (6) 发送——生产未装配发送器 → SenderUnavailable；开发模式日志输出码（dev mock，不发真实短信）
        if (_sender is not null)
        {
            try
            {
                await _sender.SendAsync(new SmsMessage(deliveryTarget, $"您的验证码是 {code}，{options.CodeTtlMinutes} 分钟内有效"), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ISmsSender 发送失败——Subject={Subject} Scene={Scene}", subject, scene);
                return new SmsCodeSendResult(false, SmsCodeSendFailure.SenderUnavailable, null);
            }
        }
        else if (options.IsProduction)
        {
            return new SmsCodeSendResult(false, SmsCodeSendFailure.SenderUnavailable, null);
        }
        else
        {
            _logger.LogWarning(
                "ISmsSender 未装配（dev mock）——短信验证码 Subject={Subject} Scene={Scene} Code={Code}",
                subject, scene, code);
        }

        return new SmsCodeSendResult(true, null, null);
    }

    /// <inheritdoc />
    public async Task<SmsCodeVerifyResult> VerifyCodeAsync(string scene, string subject, string code,
        CancellationToken ct = default)
    {
        // (0) 场景校验——未知场景 fail-closed（不落入任何频控键，防换场景绕限）
        if (!_options.Value.Scenes.ContainsKey(scene))
            return new SmsCodeVerifyResult(false, SmsCodeFailure.UnknownScene);

        // (0.5) 作用域解析——null = 租户缺失
        var scope = _scopeResolver.GetScopePrefix();
        if (scope is null)
            return new SmsCodeVerifyResult(false, SmsCodeFailure.ScopeMissing);

        var now = DateTime.UtcNow;
        var options = _options.Value;

        // (1) IP 维度频控——send + verify 共享单桶（smscode:ip:{scope}:{ip}，fail-closed unknown 桶；
        //     审核 P1 修复：verify 亦消费 IP 桶——防攻击者换 subject 无限枚举 verify 面；
        //     与 SendCodeAsync 同一配额（SmsHourlyLimitPerIp），单向打通即双向封口）
        var ipAddress = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
        var ipKey = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress;
        if (_rateLimit is not null
            && !_rateLimit.TryAcquire($"smscode:ip:{scope}:{ipKey}", options.SmsHourlyLimitPerIp, TimeSpan.FromHours(1), out _))
            return new SmsCodeVerifyResult(false, SmsCodeFailure.RateLimited);

        // (2) 校验频控——verify 调用计数（15min 窗，IRateLimitCheck 点检查——修正 AuthCenter 缺陷落点：
        //     计数调用而非活跃记录，防"成功消费后换新码无限试"）
        if (_rateLimit is not null
            && !_rateLimit.TryAcquire($"smscode:verify:{scope}:{scene}:{subject}", options.VerifyAttemptsPerWindow, TimeSpan.FromMinutes(15), out _))
            return new SmsCodeVerifyResult(false, SmsCodeFailure.RateLimited);

        // (2) 取最新未消费记录（不过滤过期——区分 NotFound/Expired 语义）
        var record = await DataService.GetLatestUnverifiedAsync(subject, scene, ct);
        if (record is null)
            return new SmsCodeVerifyResult(false, SmsCodeFailure.NotFound);

        // (3) 过期判定
        if (record.ExpireAt <= now)
            return new SmsCodeVerifyResult(false, SmsCodeFailure.Expired);

        // (4) 恒定时间比对（X' 裁定正确形态：SHA256 小写 hex + FixedTimeEquals）
        if (!VerificationCodeEngine.VerifyHash(code, record.CodeHash))
        {
            // 失败计数 CAS 自增——达到阈值作废该码（M3 闭环）
            await DataService.IncrementFailedAttemptsAsync(record.Id, ct);
            var updated = await DataService.GetLatestUnverifiedAsync(subject, scene, ct);
            if (updated is not null
                && (updated.FailedAttempts ?? 0) >= options.FailedVerifyInvalidateThreshold)
                await DataService.InvalidateActiveAsync(subject, scene, ct);
            return new SmsCodeVerifyResult(false, SmsCodeFailure.Mismatch);
        }

        // (5) 单次消费 CAS——成功置 IsVerified=true（防重放）
        await DataService.MarkVerifiedAsync(record.Id, ct);
        return new SmsCodeVerifyResult(true, null);
    }

    /// <summary>拆分 captchaToken（格式：<c>"{challengeId}:{answer}"</c>）——格式不符返回 (null, null) 判 CaptchaFailed。</summary>
    private static (string? ChallengeId, string? Answer) SplitCaptchaToken(string? captchaToken)
    {
        if (string.IsNullOrWhiteSpace(captchaToken))
            return (null, null);
        var separatorIndex = captchaToken.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == captchaToken.Length - 1)
            return (null, null);
        var challengeId = captchaToken[..separatorIndex];
        var answer = captchaToken[(separatorIndex + 1)..];
        return string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(answer)
            ? (null, null)
            : (challengeId, answer);
    }
}