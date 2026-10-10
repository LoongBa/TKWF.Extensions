using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 登录保护服务——AuthLoginAttempt 记录 + 限流窗口计数 + 最近尝试查询（方案 §5.6）。
/// <para>只增语义：仅经 <see cref="AuthLoginAttemptEntityDataService.CreateAsync"/> 追加写，无 Update/Delete 路径（SecurityLog 先例）。</para>
/// <para>限流判定按 authType 独立窗口：联邦快捷（<see cref="AuthTypes.Federated"/>——T5 2026-10-09 替代原微信
/// <c>AuthTypes.Wechat</c>）60s 滑窗 ≥
/// <c>LoginProtectionOptions.OAuthPerMinutePerIp</c>；口令兑换（<see cref="AuthTypes.Redeem"/>）60min 滑窗 ≥
/// <c>LoginProtectionOptions.RedeemPerHour</c>；短信（<see cref="AuthTypes.Sms"/>）返回 false——短信频控由
/// <see cref="SmsVerificationService"/> 经 SmsRecordEntity 拥有（重发间隔 60s/小时 5 条/天 20 条/IP 20 条/校验 5 次每小时），
/// 此处不重复判定，避免双写源；未知 authType 返回 false（fail-open，仅审计不拦截）。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;IAuthLoginAttemptService, AuthLoginAttemptService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class AuthLoginAttemptService : DomainServiceBase, IAuthLoginAttemptService
{
    private readonly IOptions<AuthCenterOptions> _options;
    private AuthLoginAttemptEntityDataService? _dataService;
    private readonly ILogger<AuthLoginAttemptService> _logger;
    private AuthLoginAttemptEntityDataService DataService => _dataService ??= User.Use<AuthLoginAttemptEntityDataService>();

    public AuthLoginAttemptService(
        IDomainUser user,
        IOptions<AuthCenterOptions> options,
        ILogger<AuthLoginAttemptService> logger)
        : base(user)
    {
        _options = options;
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

    /// <inheritdoc cref="IAuthLoginAttemptService.IsRateLimitedAsync(string,string,System.Threading.CancellationToken)"/>
    public Task<bool> IsRateLimitedAsync(string userIdentity, string authType, CancellationToken ct = default)
        => IsRateLimitedAsync(userIdentity, null, authType, ct);

    /// <inheritdoc cref="IAuthLoginAttemptService.IsRateLimitedAsync(string,string?,string,System.Threading.CancellationToken)"/>
    public async Task<bool> IsRateLimitedAsync(string userIdentity, string? ipAddress, string authType, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var protection = _options.Value.LoginProtection;

        return authType switch
        {
            // 联邦快捷（T5 2026-10-09 替代原 wechat）：60s 滑窗计数（identity+authType）≥ OAuthPerMinutePerIp → 限流
            AuthTypes.Federated =>
                await DataService.CountInWindowAsync(userIdentity, authType, now.AddSeconds(-60), ct)
                    >= protection.OAuthPerMinutePerIp,

            // 口令兑换：60min 滑窗计数 ≥ RedeemPerHour → 限流
            AuthTypes.Redeem =>
                await DataService.CountInWindowAsync(userIdentity, authType, now.AddMinutes(-60), ct)
                    >= protection.RedeemPerHour,

            // 密码（V0.9.0 B.9）：双维度（P0-NEW-2 修订闭环）——
            //   ① 60s 滑窗按 UId/Phone ≥ PasswordPerMinutePerSubject（防单账号爆破）
            //   ② 60min 滑窗按 IP ≥ PasswordPerHourPerIp（防分布式多账号单 IP 爆破；ipAddress 空则退化单维度）
            AuthTypes.Password =>
                (await DataService.CountInWindowAsync(userIdentity, authType, now.AddSeconds(-60), ct)
                    >= protection.PasswordPerMinutePerSubject)
                || (await DataService.CountInWindowByIpAsync(ipAddress, authType, now.AddMinutes(-60), ct)
                    >= protection.PasswordPerHourPerIp),

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
