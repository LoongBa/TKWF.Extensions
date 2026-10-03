using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 登录保护服务——AuthLoginAttempt 记录 + 限流窗口计数 + 最近尝试查询（方案 §5.6）。
/// <para>只增语义：仅经 <see cref="AuthLoginAttemptEntityDataService.CreateAsync"/> 追加写，无 Update/Delete 路径（SecurityLog 先例）。</para>
/// <para>限流判定按 authType 独立窗口：微信 OAuth（<see cref="AuthTypes.Wechat"/>）60s 滑窗 ≥
/// <c>LoginProtectionOptions.OAuthPerMinutePerIp</c>；口令兑换（<see cref="AuthTypes.Redeem"/>）60min 滑窗 ≥
/// <c>LoginProtectionOptions.RedeemPerHour</c>；短信（<see cref="AuthTypes.Sms"/>）返回 false——短信频控由
/// <see cref="SmsVerificationService"/> 经 SmsRecordEntity 拥有（重发间隔 60s/小时 5 条/天 20 条/IP 20 条/校验 5 次每小时），
/// 此处不重复判定，避免双写源；未知 authType 返回 false（fail-open，仅审计不拦截）。</para>
/// <para>internal sealed（对齐 Identity UserStore 先例——DataService 为 internal，public 构造器会 CS0051）。</para>
/// </summary>
internal sealed class AuthLoginAttemptService : IAuthLoginAttemptService
{
    private readonly IOptions<AuthCenterOptions> _options;
    private AuthLoginAttemptEntityDataService? _dataService;
    private readonly IDomainUser _user;
    private readonly ILogger<AuthLoginAttemptService> _logger;
    private AuthLoginAttemptEntityDataService DataService => _dataService ??= _user.Use<AuthLoginAttemptEntityDataService>();

    public AuthLoginAttemptService(
        IOptions<AuthCenterOptions> options,
        IDomainUser user,
        ILogger<AuthLoginAttemptService> logger)
    {
        _options = options;
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordAttemptAsync(AuthLoginAttemptEntity attempt, CancellationToken ct = default)
    {
        // 追加写（append-only）；AttemptTime 默认值时补 UTC 当前时间（实体默认值语义兜底）
        if (attempt.AttemptTime == default)
            attempt.AttemptTime = DateTime.UtcNow;
        await DataService.CreateAsync(attempt, ct);
    }

    /// <inheritdoc />
    public async Task<bool> IsRateLimitedAsync(string userIdentity, string authType, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var protection = _options.Value.LoginProtection;

        return authType switch
        {
            // 微信 OAuth：60s 滑窗计数（identity+authType）≥ OAuthPerMinutePerIp → 限流
            AuthTypes.Wechat =>
                await DataService.CountInWindowAsync(userIdentity, authType, now.AddSeconds(-60), ct)
                    >= protection.OAuthPerMinutePerIp,

            // 口令兑换：60min 滑窗计数 ≥ RedeemPerHour → 限流
            AuthTypes.Redeem =>
                await DataService.CountInWindowAsync(userIdentity, authType, now.AddMinutes(-60), ct)
                    >= protection.RedeemPerHour,

            // 短信：返回 false——短信发送/校验频控由 SmsVerificationService 经 SmsRecordEntity 拥有
            //（SmsResendIntervalSeconds=60 / SmsHourlyLimitPerPhone=5 / SmsDailyLimitPerPhone=20 /
            //  SmsHourlyLimitPerIp=20 / SmsVerifyAttemptsPerHour=5），此处委托不重复判定。
            AuthTypes.Sms => false,

            // 未知 authType：fail-open——仅审计记录，不拦截（限流策略按 authType 独立，未配置类型不设窗口）
            _ => false
        };
    }

    /// <inheritdoc />
    public async Task<List<AuthLoginAttemptEntity>> GetRecentAttemptsAsync(
        string userIdentity, string authType, int count, CancellationToken ct = default)
        => await DataService.EntitySelectAsync(
            e => e.UserIdentity == userIdentity && e.AuthType == authType,
            0, count,
            q => q.OrderByDescending(e => e.AttemptTime),
            ct);
}
