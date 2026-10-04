using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 微信 OAuth 登录编排门面实现（V4.10.55 ADR92，T3 闭环）——表现层零编排终态：
/// 控制器改 <c>User.Use&lt;IWechatLoginService&gt;().LoginAsync(...)</c>，不再
/// <c>[FromServices] IEnumerable&lt;IAuthenticationProvider&gt;</c> 直注集合（帧外枚举抛守卫）。
/// <para>门面 ctor 注入 <c>IEnumerable&lt;IAuthenticationProvider&gt;</c>——经 <c>User.Use&lt;门面&gt;()</c>
/// 帧内创建时集合守卫工厂（<c>TryAddEnumerableConstructible</c>）经 CurrentAopUser 供给 IDomainUser 构造。
/// 选区后委托 Provider.AuthenticateAsync，失败语义透传。</para>
/// <para>注册：<c>AddConstructibleService&lt;IWechatLoginService, WechatLoginService&gt;</c>。<c>[DiContractIgnore]</c>：豁免 DI001。</para>
/// </summary>
[DiContractIgnore]
internal sealed class WechatLoginService : DomainServiceBase, IWechatLoginService
{
    private readonly IReadOnlyList<IAuthenticationProvider> _providers;
    private readonly ILogger<WechatLoginService> _logger;

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
    public async Task<ProviderAuthenticateResult> LoginAsync(string wechatCode, string wechatScope, CancellationToken ct = default)
    {
        var provider = _providers.FirstOrDefault(p => p.AuthType == AuthTypes.Wechat);
        if (provider == null)
        {
            var fail = new ProviderAuthenticateResult(false, null, "WECHAT_PROVIDER_NOT_ENABLED");
            _logger.LogWarning("微信登录编排失败——WeChat Provider 未启用（EnabledAuthTypes 配置缺失 wechat）");
            return fail;
        }

        return await provider.AuthenticateAsync(
            new ProviderAuthenticateContext(WechatCode: wechatCode, WechatScope: wechatScope),
            ct);
    }
}