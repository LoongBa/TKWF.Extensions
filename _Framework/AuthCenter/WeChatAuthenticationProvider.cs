using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 微信 OAuth 认证 Provider（内置，认证后就绪便捷路径——公众号网页授权 + 开放平台扫码双形态，方案 §5.5）。
/// <para>流程：微信授权 code → WeChatApiClient.GetOpenIdAsync → 查账号（按 openid 绑定列）→ 有账号登录 / 无账号建账号并绑定 openid。</para>
/// <para>scope 判定绑定列：snsapi_base → WechatMpOpenId（网页授权）；snsapi_login → WechatWebOpenId（扫码）；默认扫码。</para>
/// <para>登录 state/票据 vs 绑定 bind_token/state 隔离不复用（Oracle I6 防绑定劫持——绑定流程归装配层，本 Provider 只做登录）。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；内部 <see cref="IWeChatApiClient"/> / DataService 经 <c>User.Use&lt;T&gt;()</c> 懒加载。
/// 注册保持 <c>TryAddEnumerable(Scoped&lt;IAuthenticationProvider, WeChatAuthenticationProvider&gt;)</c>——多实现集合，
/// AddConstructibleService 单实现不适用。<c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class WeChatAuthenticationProvider : DomainServiceBase, IAuthenticationProvider
{
    private IWeChatApiClient? _weChatApi;
    private AuthAccountEntityDataService? _accountDataService;
    private readonly ILogger<WeChatAuthenticationProvider> _logger;

    private IWeChatApiClient WeChatApi => _weChatApi ??= User.Use<IWeChatApiClient>();
    private AuthAccountEntityDataService AccountDataService => _accountDataService ??= User.Use<AuthAccountEntityDataService>();

    public WeChatAuthenticationProvider(IDomainUser user, ILogger<WeChatAuthenticationProvider> logger) : base(user)
    {
        _logger = logger;
    }

    public string AuthType => AuthTypes.Wechat;

    public async Task<ProviderAuthenticateResult> AuthenticateAsync(ProviderAuthenticateContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.WechatCode))
            return new ProviderAuthenticateResult(false, null, "WECHAT_CODE_REQUIRED");

        string openId;
        try
        {
            // appId 参数预留（多应用场景 Provider 扩展时按 context 扩展定位凭证；当前按 platform 模糊查凭证）
            openId = await WeChatApi.GetOpenIdAsync("", context.WechatCode, ct);
        }
        catch (Exception ex) when (ex is AuthenticationException or InvalidOperationException)
        {
            return new ProviderAuthenticateResult(false, null, ex.Message);
        }

        var isMpScope = string.Equals(context.WechatScope, "snsapi_base", StringComparison.OrdinalIgnoreCase);

        // 查账号：按 openid 绑定列（公众号 vs 网站应用）
        var account = isMpScope
            ? await AccountDataService.GetByWechatMpOpenIdAsync(openId, ct)
            : await AccountDataService.GetByWechatWebOpenIdAsync(openId, ct);
        if (account == null && !isMpScope)
        {
            // 扫码兜底：网页授权列也可能已绑定（双形态归并）
            account = await AccountDataService.GetByWechatMpOpenIdAsync(openId, ct);
        }

        if (account != null)
        {
            if (!account.IsEnabled) return new ProviderAuthenticateResult(false, null, "ACCOUNT_DISABLED");
            return new ProviderAuthenticateResult(true, account.UId, null, AuthLevel.Wechat);
        }

        // 无账号 → 建账号（openid 绑定——UnionId 由装配层经 GetUserInfoAsync 补充；
        // Phone 为 null——微信便捷登录无手机号账号（唯一索引对 NULL 放行；短信绑定补齐后回填））
        var created = new AuthAccountEntity
        {
            UId = AccountIdGenerator.NewUId(),
            Phone = null,
            AuthLevel = (int)AuthLevel.Wechat,
            TokenVersion = 0
        };
        if (isMpScope) created.WechatMpOpenId = openId;
        else created.WechatWebOpenId = openId;
        created.Nickname = null;

        try
        {
            await AccountDataService.CreateAsync(created, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "微信登录建账号并发冲突——重查（openid 绑定兜底）");
            var existing = isMpScope
                ? await AccountDataService.GetByWechatMpOpenIdAsync(openId, ct)
                : await AccountDataService.GetByWechatWebOpenIdAsync(openId, ct);
            if (existing != null) return new ProviderAuthenticateResult(true, existing.UId, null, AuthLevel.Wechat);
            return new ProviderAuthenticateResult(false, null, "ACCOUNT_CREATE_FAILED");
        }
        return new ProviderAuthenticateResult(true, created.UId, null, AuthLevel.Wechat);
    }
}
