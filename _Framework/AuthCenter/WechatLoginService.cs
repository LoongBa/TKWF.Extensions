using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 微信 OAuth 登录编排门面实现（V4.10.55 ADR92，T3 闭环——表现层零编排终态；V0.6.0 Oracle P0-1 升级：
/// 返回 <see cref="LoginResult"/>——门面内完成 认证→取账号→签发 全编排，端点只调一个门面，不串多门面）。
/// <para>门面 ctor 注入 <c>IEnumerable&lt;IAuthenticationProvider&gt;</c> + <c>IAuthAccountQueryService</c> +
/// <c>ITokenService</c>——经 <c>User.Use&lt;门面&gt;()</c> 帧内创建时集合守卫工厂（<c>TryAddEnumerableConstructible</c>）
/// 与守卫工厂（<c>AddConstructibleService</c>）经 CurrentAopUser 供给 IDomainUser 构造（链式帧内解析）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IWechatLoginService, WechatLoginService&gt;</c>。<c>[DiContractIgnore]</c>：豁免 DI001。</para>
/// </summary>
[DiContractIgnore]
internal sealed class WechatLoginService : DomainServiceBase, IWechatLoginService
{
    private readonly IReadOnlyList<IAuthenticationProvider> _providers;
    private readonly ILogger<WechatLoginService> _logger;

    // DI004 铁律：领域服务间调用经 User.Use<T>() 懒加载（禁构造注入——IAuthAccountQueryService/ITokenService 为守卫工厂，
    // 帧内经基类 User 解析；V0.6.0 Oracle P0-1 门面内编排 认证→取账号→签发）
    private IAuthAccountQueryService? _accountQueryService;
    private IAuthAccountQueryService AccountQueryService => _accountQueryService ??= User.Use<IAuthAccountQueryService>();
    private ITokenService? _tokenService;
    private ITokenService TokenService => _tokenService ??= User.Use<ITokenService>();

    public WechatLoginService(
        IDomainUser user,
        IEnumerable<IAuthenticationProvider> providers,
        ILogger<WechatLoginService> logger)
        : base(user)
    {
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(string wechatCode, string wechatScope, CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.AuthType == AuthTypes.Wechat);
        if (provider == null)
        {
            var fail = new LoginResult(false, null, null, "WECHAT_PROVIDER_NOT_ENABLED");
            _logger.LogWarning("微信登录编排失败——WeChat Provider 未启用（EnabledAuthTypes 配置缺失 wechat）");
            return fail;
        }

        var result = await provider.AuthenticateAsync(
            new ProviderAuthenticateContext(WechatCode: wechatCode, WechatScope: wechatScope),
            ct);
        if (!result.Success)
        {
            _logger.LogDebug("微信登录认证失败——{FailReason}", result.FailReason);
            return new LoginResult(false, null, null, result.FailReason);
        }

        // 取持久化账号字段（TeacherVerified/AuthLevel——Provider 建账号后回读；缺失视为编排异常）
        var account = await AccountQueryService.GetByUIdAsync(result.UserId, ct);
        if (account is null)
        {
            _logger.LogWarning("微信登录编排失败——账号查询返回 null（UserId={UserId}）", result.UserId);
            return new LoginResult(false, null, null, "ACCOUNT_NOT_FOUND");
        }

        var token = await TokenService.IssueTokenAsync(new TokenIssueRequest(
            account.UId, AuthTypes.Wechat, account.AuthLevel, account.TeacherVerified), ct);
        _logger.LogDebug("微信登录成功——UserId={UserId}", account.UId);
        return new LoginResult(true, account.UId, token, null);
    }
}