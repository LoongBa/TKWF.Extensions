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

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 短信验证码登录编排门面实现（V4.10.55 ADR92，T3 闭环——表现层零编排终态；B3b MapGenerated 生成化：
/// 返回 <see cref="TokenResponse"/>——门面内完成 认证→取账号→签发 全编排，失败抛
/// <see cref="DomainException"/>（400，消息 = 语义码），异常直达中间件无 handler 映射）。
/// <para>门面 ctor 注入 <c>IEnumerable&lt;IAuthenticationProvider&gt;</c> + <c>IAuthAccountQueryService</c> +
/// <c>ITokenService</c>——经 <c>User.Use&lt;门面&gt;()</c> 帧内创建时集合守卫工厂（<c>TryAddEnumerableConstructible</c>）
/// 与守卫工厂（<c>AddConstructibleService</c>）经 CurrentAopUser 供给 IDomainUser 构造（链式帧内解析）。</para>
/// <para>注册：<c>AddConstructibleService&lt;ISmsLoginService, SmsLoginService&gt;</c>（接口可构造守卫工厂 +
/// 实现类 throw-factory）。<c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
[GenerateController]
public sealed class SmsLoginService : DomainServiceBase, ISmsLoginService
{
    private readonly IReadOnlyList<IAuthenticationProvider> _providers;
    private readonly IOptions<AuthCenterOptions> _options;
    private readonly ILogger<SmsLoginService> _logger;

    // DI004 铁律：领域服务间调用经 User.Use<T>() 懒加载（禁构造注入——IAuthAccountQueryService/ITokenService 为守卫工厂，
    // 帧内经基类 User 解析；V0.6.0 Oracle P0-1 门面内编排 认证→取账号→签发）
    private IAuthAccountQueryService? _accountQueryService;
    private IAuthAccountQueryService AccountQueryService => _accountQueryService ??= User.Use<IAuthAccountQueryService>();
    private ITokenService? _tokenService;
    private ITokenService TokenService => _tokenService ??= User.Use<ITokenService>();

    public SmsLoginService(
        IDomainUser user,
        IEnumerable<IAuthenticationProvider> providers,
        IOptions<AuthCenterOptions> options,
        ILogger<SmsLoginService> logger)
        : base(user)
    {
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>EnabledAuthTypes fail-closed 判定（V0.9.0 P1-1/P2-6——集合外 Provider 不接线；先过滤再选区）。</summary>
    private bool IsEnabled(string authType)
        => _options.Value.EnabledAuthTypes.Contains(authType, StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<TokenResponse> LoginAsync(SmsLoginRequest request, CancellationToken ct = default)
    {
        var (phone, code) = (request.Phone, request.Code);
        // V0.9.0 (P2-6 顺序裁定)：先按 EnabledAuthTypes.Contains(AuthType) 过滤集合 → 再 AuthType match（fail-closed）
        var provider = _providers.FirstOrDefault(p =>
            p.AuthType == AuthTypes.Sms && IsEnabled(p.AuthType));
        if (provider == null)
        {
            _logger.LogWarning("短信登录编排失败——SMS Provider 未启用（EnabledAuthTypes 配置缺失 sms）");
            throw new DomainException("SMS_PROVIDER_NOT_ENABLED") { ErrorCode = "SMS_PROVIDER_NOT_ENABLED" };
        }

        var result = await provider.AuthenticateAsync(
            new ProviderAuthenticateContext(Phone: phone, Code: code),
            ct);
        if (!result.Success)
        {
            _logger.LogDebug("短信登录认证失败——{FailReason}", result.FailReason);
            throw new DomainException(result.FailReason ?? "SMS_LOGIN_FAILED") { ErrorCode = result.FailReason ?? "SMS_LOGIN_FAILED" };
        }

        // 取持久化账号（签发需账号锚点；AuthLevel 由 AuthFactor 派生——V0.11.0 T9）
        var account = await AccountQueryService.GetByUIdAsync(result.UserId, ct);
        if (account is null)
        {
            _logger.LogWarning("短信登录编排失败——账号查询返回 null（UserId={UserId}）", result.UserId);
            throw new DomainException("ACCOUNT_NOT_FOUND") { ErrorCode = "ACCOUNT_NOT_FOUND" };
        }

        var authLevel = await User.Use<IAuthFactorService>().GetAuthLevelAsync(account.UId, ct);
        var token = await TokenService.IssueTokenAsync(new TokenIssueRequest(
            account.UId, AuthTypes.Sms, authLevel), ct);
        _logger.LogDebug("短信登录成功——UserId={UserId}", account.UId);
        return new TokenResponse(token.AccessToken, token.RefreshToken, "Bearer", token.ExpiresIn);
    }
}