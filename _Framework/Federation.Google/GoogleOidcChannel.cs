using System;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Google;

/// <summary>
/// Google OIDC 通道（<c>google_oidc</c>——M2 方案：public sub，OIDC 参考实现基线）。
/// <para>继承 M1 基座 <see cref="OidcChannelBase"/>——仅填 Google 固定端点（<c>Defaults</c>）+ public 通配
/// <c>BuildChannelId = "google_oidc:*"</c>（Oracle P1-5：构造责任在 channel 内部）；凭证/覆盖项经
/// <c>GoogleOptions.Channels</c> 选区合并（<c>MergeConfig</c>——M1 基座工具）。</para>
/// <para>认证流：authorize URL 构造 + state 校验归装配层（基座 <c>AuthenticateAsync</c> 不感知 state）；
/// code→token→id_token JWKS 验签→sub（信任根——Google JWKS RS256）。</para>
/// </summary>
public sealed class GoogleOidcChannel : OidcChannelBase
{
    private readonly OidcPlatformConfig _config;

    /// <summary>构造——Google 固定端点 Defaults + Channels 选区凭证合并（对齐 WeChat 先例 Channels[0] 默认）。</summary>
    public GoogleOidcChannel(
        IDomainUser user,
        OidcChannelFlow flow,
        IOptions<GoogleOptions> options)
        : base(user, flow)
    {
        var cfg = options?.Value.Channels.Count > 0 ? options.Value.Channels[0] : null;
        _config = MergeConfig(Defaults(), cfg is null ? null : new OidcPlatformConfig
        {
            ChannelId = cfg.ChannelId,
            ClientId = cfg.ClientId,
            ClientSecret = cfg.ClientSecret,
            TokenIssuers = cfg.TokenIssuers,
        });
    }

    /// <inheritdoc />
    public override string ChannelType => "google_oidc";

    /// <inheritdoc />
    protected override OidcPlatformConfig EffectiveConfig => _config;

    /// <inheritdoc />
    protected override string BuildChannelId(OidcPlatformConfig config) => $"{ChannelType}:*";   // public 通配（N2 §3.3）

    /// <inheritdoc />
    protected override OidcPlatformConfig Defaults() => new()
    {
        Platform = "google",
        AuthorizeUri = "https://accounts.google.com/o/oauth2/v2/auth",
        TokenUri = "https://oauth2.googleapis.com/token",
        UserInfoUri = "https://openidconnect.googleapis.com/v1/userinfo",
        JwksUri = "https://www.googleapis.com/oauth2/v3/certs",
        DiscoveryUri = "https://accounts.google.com/.well-known/openid-configuration",
        Scopes = ["openid", "email", "profile"],
        UsePkce = true,
        TokenIssuers = ["https://accounts.google.com"],
        FetchUserInfo = false,
    };
}