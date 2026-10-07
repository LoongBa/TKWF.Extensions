using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Google;

/// <summary>
/// Google OIDC 通道（<c>google_oidc</c>——M2 方案：public sub，OIDC 参考实现基线）。
/// <para>继承 M1 基座 <see cref="OidcChannelBase"/>——仅填 Google 固定端点（<c>Defaults</c>）+ public 通配
/// <c>BuildChannelId = "google_oidc:*"</c>（Oracle P1-5：构造责任在 channel 内部）；凭证/覆盖项经
/// <see cref="GoogleChannelSource"/> 投影（v0.3.0 多通道联邦——ClientId/ClientSecret 走公共列 AppId/AppSecret、
/// TokenIssuers 进 Extra，基座 <see cref="OidcChannelBase.FromChannel"/> 重建 + Defaults 合并）。</para>
/// <para>⚠️ Google 显式单实例语义（Oracle P1-3/S4）：ctor 签名跟随统一改造（一致性），运行时只配单通道
/// （GoogleOptions.Channels[0] 场景经 <see cref="GoogleChannelSource"/> 投影）——文档标注。</para>
/// <para>认证流：authorize URL 构造 + state 校验归装配层（基座 <c>AuthenticateAsync</c> 不感知 state）；
/// code→token→id_token JWKS 验签→sub（信任根——Google JWKS RS256）。</para>
/// </summary>
public sealed class GoogleOidcChannel : OidcChannelBase
{
    /// <summary>构造——channel 由工厂预取（POCO 非域服务）；null = 集合模板实例。</summary>
    public GoogleOidcChannel(IDomainUser user, OidcChannelFlow flow, ChannelConfig? channel = null)
        : base(user, flow, channel)
    {
    }

    /// <inheritdoc />
    public override string ChannelType => "google_oidc";

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
