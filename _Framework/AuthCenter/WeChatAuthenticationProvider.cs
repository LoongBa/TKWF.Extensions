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
/// <para>流程：微信授权 code → WeChatApiClient.GetOpenIdAsync → 查联邦映射（<see cref="ISsoChannelMapService"/>
/// 按 channelId=wechat_mp/wechat_web + openId）→ 有 uid 登录 / 无则建账号 + 建立映射。</para>
/// <para>V0.9.0 联邦 Id 归一化（ADR A.8——WechatMpOpenId/WechatWebOpenId/UnionId 三列删）：微信 openid 绑定迁
/// <see cref="PlatformAccountMapEntity"/>（<see cref="PlatformAccountMapService"/> 的 SSO 联邦映射，<c>UX_PlatformAccountMap_Channel</c> 复合唯一）；
/// 查号改 <c>GetByChannelAsync(channelId, openId)</c>，建号改 <c>LinkAsync(uid, channelId, openId)</c>——并发兜底由
/// <see cref="PlatformAccountMapService.LinkAsync"/> 内部 UX catch（硬化，P1-3），Provider 不再自包 catch。</para>
/// <para>scope 判定 channelId：snsapi_base → wechat_mp（网页授权）；snsapi_login → wechat_web（扫码）；默认扫码。</para>
/// <para>登录 state/票据 vs 绑定 bind_token/state 隔离不复用（Oracle I6 防绑定劫持——绑定流程归装配层，本 Provider 只做登录）。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；内部 <see cref="IWeChatApiClient"/> / <see cref="ISsoChannelMapService"/> 经 <c>User.Use&lt;T&gt;()</c> 懒加载。
/// 注册保持 <c>TryAddEnumerableConstructible&lt;IAuthenticationProvider, WeChatAuthenticationProvider&gt;</c>（V4.10.55 ADR92——集合版守卫工厂）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class WeChatAuthenticationProvider : DomainServiceBase, IAuthenticationProvider
{
    /// <summary>公众号网页授权 channelId（snsapi_base）。</summary>
    public const string ChannelWechatMp = "wechat_mp";

    /// <summary>开放平台扫码 channelId（snsapi_login）。</summary>
    public const string ChannelWechatWeb = "wechat_web";

    private IWeChatApiClient? _weChatApi;
    private ISsoChannelMapService? _channelMap;
    private AuthAccountEntityDataService? _accountDataService;
    private readonly ILogger<WeChatAuthenticationProvider> _logger;

    private IWeChatApiClient WeChatApi => _weChatApi ??= User.Use<IWeChatApiClient>();
    private ISsoChannelMapService ChannelMap => _channelMap ??= User.Use<ISsoChannelMapService>();
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
        var channelId = isMpScope ? ChannelWechatMp : ChannelWechatWeb;

        // 查联邦映射：(channelId, openId) → uid（V0.9.0 归一化——替代原 openid 列唯一查号）
        var mapped = await ChannelMap.GetByChannelAsync(channelId, openId, ct);
        var uid = mapped?.UId;
        if (uid != null)
        {
            var account = await AccountDataService.GetByUIdAsync(uid, ct);
            if (account == null) return new ProviderAuthenticateResult(false, null, "ACCOUNT_NOT_FOUND");
            if (!account.IsEnabled) return new ProviderAuthenticateResult(false, null, "ACCOUNT_DISABLED");
            if (account.IsFrozenEffective) return new ProviderAuthenticateResult(false, null, "ACCOUNT_FROZEN");   // V0.9.0 冻结检查（ADR 决策 3）
            return new ProviderAuthenticateResult(true, account.UId, null, AuthLevel.Federated);
        }

        // 无映射 → 建账号（Phone 为 null——微信便捷登录无手机号账号（唯一索引对 NULL 放行；短信绑定补齐后回填））
        // + 建立联邦映射（concurrent UX catch 由 PlatformAccountMapService.LinkAsync 硬化兜底，P1-3）
        var created = new AuthAccountEntity
        {
            UId = AccountIdGenerator.NewUId(),
            Phone = null,
            AuthLevel = (int)AuthLevel.Federated,
            TokenVersion = 0
        };

        try
        {
            await AccountDataService.CreateAsync(created, ct);
            await ChannelMap.LinkAsync(created.UId, channelId, openId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "微信登录建账号/建立映射并发冲突——重查（联邦映射 UX_PlatformAccountMap_Channel 兜底）");
            var existing = await ChannelMap.GetByChannelAsync(channelId, openId, ct);
            if (existing != null) return new ProviderAuthenticateResult(true, existing.UId, null, AuthLevel.Federated);
            return new ProviderAuthenticateResult(false, null, "ACCOUNT_CREATE_FAILED");
        }
        return new ProviderAuthenticateResult(true, created.UId, null, AuthLevel.Federated);
    }
}