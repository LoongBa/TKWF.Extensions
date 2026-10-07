using System.Collections.Generic;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Microsoft;

/// <summary>
/// Microsoft Entra ID OIDC 通道（<c>microsoft_oidc</c>——M2 方案：pairwise sub + tenant 配置化）。
/// <para>继承 M1 基座 <see cref="OidcChannelBase"/>——Defaults 按 Tenant 模板化端点（common/consumers/organizations/{id}）；
/// **pairwise sub → channel_id 复合编码 <c>BuildChannelId = $"{ChannelType}:{{client_id}}"</c>**（Oracle P1-2：
/// ChannelId 计算属性自动拼接，消费方只配 ClientId——不依赖手动拼接；N2 §3.3 定案）。</para>
/// <para>v0.3.0 多通道联邦：凭证/覆盖项经 <see cref="MicrosoftChannelSource"/> 投影（ClientId/ClientSecret 走公共列
/// AppId/AppSecret、Tenant/TokenIssuers 进 Extra）；<see cref="EffectiveConfig"/> 覆写——按
/// <c>Extra["Tenant"]</c>（缺省 common）模板化端点 + <see cref="OidcChannelBase.FromChannel"/> 合并。</para>
/// <para>⚠️ tenant 受众维度：common/consumers/organizations 授权后 <c>iss</c> 含**实际租户 GUID**
/// （非字面 tenant 值）——TokenIssuer 白名单须通配/正则匹配（<c>https://login.microsoftonline.com/.+/v2.0</c>，
/// Oracle P1-1 + M1 issuer 容错）。</para>
/// </summary>
public sealed class MicrosoftOidcChannel : OidcChannelBase
{
    /// <summary>构造——channel 由工厂预取（POCO 非域服务）；null = 集合模板实例。</summary>
    public MicrosoftOidcChannel(IDomainUser user, OidcChannelFlow flow, ChannelConfig? channel = null)
        : base(user, flow, channel)
    {
    }

    /// <inheritdoc />
    public override string ChannelType => "microsoft_oidc";

    /// <inheritdoc />
    protected override string BuildChannelId(OidcPlatformConfig config) => $"{ChannelType}:{config.ClientId}";   // pairwise 复合编码（Oracle P1-2）

    /// <inheritdoc />
    /// <summary>有效配置——按 <c>_channel.Extra["Tenant"]</c>（缺省 common）模板化端点 + FromChannel 合并；
    /// 模板实例（_channel null）返回 Defaults（common）。</summary>
    protected override OidcPlatformConfig EffectiveConfig
    {
        get
        {
            if (_channel is null)
                return Defaults();   // 集合模板实例（无选区配置）
            var tenant = GetChannelValue(OidcChannelConfigKeys.Tenant, "common") ?? "common";
            return MergeConfig(Defaults(tenant), FromChannel(_channel));
        }
    }

    /// <inheritdoc />
    protected override OidcPlatformConfig Defaults() => Defaults("common");

    /// <summary>按 tenant 模板化的 Microsoft 端点（{tenant} 占位——common/consumers/organizations/{id}）。</summary>
    private static OidcPlatformConfig Defaults(string tenant)
    {
        var t = string.IsNullOrWhiteSpace(tenant) ? "common" : tenant;
        return new OidcPlatformConfig
        {
            Platform = "microsoft",
            AuthorizeUri = $"https://login.microsoftonline.com/{t}/oauth2/v2.0/authorize",
            TokenUri = $"https://login.microsoftonline.com/{t}/oauth2/v2.0/token",
            UserInfoUri = "https://graph.microsoft.com/oidc/userinfo",
            JwksUri = $"https://login.microsoftonline.com/{t}/discovery/v2.0/keys",
            DiscoveryUri = $"https://login.microsoftonline.com/{t}/v2.0/.well-known/openid-configuration",
            Scopes = ["openid", "profile", "email"],
            UsePkce = true,
            // ⚠️ common/consumers/organizations 授权后 iss 含实际租户 GUID——通配/正则匹配（M2-P1-1）
            TokenIssuers = [$"https://login.microsoftonline.com/{t}/v2.0",
                            $"^https://login\\.microsoftonline\\.com/[^/]+/v2\\.0$"],
            FetchUserInfo = false,
        };
    }
}
