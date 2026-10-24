using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 密码登录编排门面实现（V0.9.0 B.9——镜像 <see cref="SmsLoginService"/>：认证→取账号→签发 全编排，表现层零编排；
/// B3b MapGenerated 生成化返回 <see cref="TokenResponse"/>——失败抛类型化异常（频控
/// <see cref="RateLimitedException"/> 429 / 业务 <see cref="DomainException"/> 400），异常直达中间件无 handler 映射）。
/// <para>门面 ctor 注入 <c>IEnumerable&lt;IAuthenticationProvider&gt;</c>——经 <c>User.Use&lt;IPasswordLoginService&gt;()</c>
/// 帧内创建时集合守卫工厂（<c>TryAddEnumerableConstructible</c>）经 CurrentAopUser 供给构造。</para>
/// <para>注册：<c>AddConstructibleService&lt;IPasswordLoginService, PasswordLoginService&gt;</c>（接口可构造守卫工厂 +
/// 实现类 throw-factory）。<c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// <para>V0.9.0（ADR-密码策略与口令协议 决策 2/3——锁定=限流）：登录前置 <see cref="IRateLimitCheck.TryAcquire"/>
/// 点检查（key <c>pwd:login:{uid}</c>——未启用扩展限流时 Initializer fallback <c>MemoryRateLimitCheck</c> 兜底；
/// 消费方显式注册 <c>SqlCountRateLimitCheck</c> 得 DB 跨实例版）；失败 = 锁定阻断（抛
/// <see cref="RateLimitedException"/> PASSWORD_RATE_LIMITED 429，不落账号状态）。
/// <c>RecordAttemptAsync</c> 审计保留（与锁定解耦——审计表 COUNT 双目的）。</para>
/// </summary>
[DiContractIgnore]
[GenerateController]
public sealed class PasswordLoginService : DomainServiceBase, IPasswordLoginService
{
    private readonly IReadOnlyList<IAuthenticationProvider> _providers;
    private readonly IOptions<AuthCenterOptions> _options;
    private readonly IRateLimitCheck _rateLimit;
    private readonly ILogger<PasswordLoginService> _logger;

    // DI004 铁律：领域服务间调用经 User.Use<T>() 懒加载（禁构造注入守卫工厂门面）
    private IAuthAccountQueryService? _accountQueryService;
    private IAuthAccountQueryService AccountQueryService => _accountQueryService ??= User.Use<IAuthAccountQueryService>();
    private ITokenService? _tokenService;
    private ITokenService TokenService => _tokenService ??= User.Use<ITokenService>();

    public PasswordLoginService(
        IDomainUser user,
        IEnumerable<IAuthenticationProvider> providers,
        IOptions<AuthCenterOptions> options,
        IRateLimitCheck rateLimit,
        ILogger<PasswordLoginService> logger)
        : base(user)
    {
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rateLimit = rateLimit ?? throw new ArgumentNullException(nameof(rateLimit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>EnabledAuthTypes fail-closed 判定（V0.9.0 P2-6——先过滤再选区）。</summary>
    private bool IsEnabled(string authType)
        => _options.Value.EnabledAuthTypes.Contains(authType, StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<TokenResponse> LoginAsync(PasswordLoginRequest request, CancellationToken ct = default)
    {
        var (identifier, clientHash, salt) = (request.Identifier, request.ClientHash, request.Salt);
        // V0.9.0 (P2-6 顺序裁定)：先按 EnabledAuthTypes.Contains(AuthType) 过滤集合 → 再 AuthType match（fail-closed）
        var provider = _providers.FirstOrDefault(p =>
            p.AuthType == AuthTypes.Password && IsEnabled(p.AuthType));
        if (provider == null)
        {
            _logger.LogWarning("密码登录编排失败——Password Provider 未启用（EnabledAuthTypes 配置缺失 password）");
            throw new DomainException("PASSWORD_PROVIDER_NOT_ENABLED") { ErrorCode = "PASSWORD_PROVIDER_NOT_ENABLED" };
        }

        // V0.9.0（ADR 决策 2/3——锁定=限流）：登录前置点检查（key 规范 pwd:login:{identifier}）——
        // TryAcquire 失败 = 锁定阻断（fail-closed）→ RateLimitedException 429（PASSWORD_RATE_LIMITED）
        var protection = _options.Value.LoginProtection;
        if (!_rateLimit.TryAcquire($"pwd:login:{identifier}", protection.PasswordPerMinutePerSubject, TimeSpan.FromMinutes(1), out _))
            throw new RateLimitedException(0, $"pwd:login:{identifier}") { ErrorCode = "PASSWORD_RATE_LIMITED" };

        var result = await provider.AuthenticateAsync(
            new ProviderAuthenticateContext(Identifier: identifier, ClientHash: clientHash, Salt: salt),
            ct);
        // P1-NEW-3 修订：密码登录尝试审计落库（成功/失败均记录——频控 IsRateLimitedAsync(AuthTypes.Password) 依赖此记录）
        await RecordAttemptAsync(identifier, result.Success, result.FailReason, ct);
        if (!result.Success)
        {
            _logger.LogDebug("密码登录认证失败——{FailReason}", result.FailReason);
            throw new DomainException(result.FailReason ?? "PASSWORD_LOGIN_FAILED") { ErrorCode = result.FailReason ?? "PASSWORD_LOGIN_FAILED" };
        }

        // 取持久化账号（签发需账号锚点；AuthLevel 由 AuthFactor 派生——V0.11.0 T9）
        var account = await AccountQueryService.GetByUIdAsync(result.UserId, ct);
        if (account is null)
        {
            _logger.LogWarning("密码登录编排失败——账号查询返回 null（UserId={UserId}）", result.UserId);
            throw new DomainException("ACCOUNT_NOT_FOUND") { ErrorCode = "ACCOUNT_NOT_FOUND" };
        }

        var authLevel = await User.Use<IAuthFactorService>().GetAuthLevelAsync(account.UId, ct);
        var token = await TokenService.IssueTokenAsync(new TokenIssueRequest(
            account.UId, AuthTypes.Password, authLevel), ct);
        _logger.LogDebug("密码登录成功——UserId={UserId}", account.UId);
        return new TokenResponse(token.AccessToken, token.RefreshToken, "Bearer", token.ExpiresIn);
    }

    /// <summary>P1-NEW-3：登录尝试审计落库（AuthLoginAttempt——append-only；频控窗口计数的数据源）。</summary>
    private async Task RecordAttemptAsync(string userIdentity, bool success, string? failReason, CancellationToken ct)
    {
        try
        {
            var attemptService = User.Use<IAuthLoginAttemptService>();
            await attemptService.RecordAttemptAsync(new AuthLoginAttemptEntity
            {
                UserIdentity = userIdentity,
                AuthType = AuthTypes.Password,
                IsSuccess = success,
                FailReason = failReason,
                AttemptTime = DateTime.UtcNow,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 审计失败不阻断登录（best-effort——对齐 AuthGrant 落授权语义）；仅记录日志
            _logger.LogWarning(ex, "密码登录尝试审计落库失败——best-effort 不阻断（UserIdentity={UserIdentity}）", userIdentity);
        }
    }
}