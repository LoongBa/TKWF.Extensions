using System;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Federation.Oidc;

/// <summary>
/// 自托管 IdP 直配通道（OidcConfiguredChannel——国外规划 §三 P2-3：Keycloak/Okta/Auth0/Authentik
/// 经通用基座配置驱动接入，零专属代码）。
/// <para>注册 <c>AddOidcFederationChannel(name, configure)</c> 时向 <c>OidcOptions.Channels</c> 追加配置，
/// 本通道从 Channels 选区（对齐 WeChat 先例 Channels[0] 默认——多实例选区归后续迭代，Oracle P0-1 修正：
/// 注册具体包装通道而非抽象基类——抽象类 DI 无法实例化运行时必炸）。</para>
/// <para>ChannelType = <c>"{Platform}_oidc"</c>（如 <c>keycloak_oidc</c>——配置 Platform 字段驱动）；
/// Defaults 返回空（完全配置驱动——自托管 IdP 端点各异无默认）。</para>
/// </summary>
public sealed class OidcConfiguredChannel : OidcChannelBase
{
    private readonly OidcPlatformConfig _config;
    private readonly string _channelType;

    /// <summary>构造——从 <c>TKWF:Federation:Oidc</c> 节 Channels 选区（Channels[0] 默认，多实例选区归后续迭代）。</summary>
    public OidcConfiguredChannel(
        IDomainUser user,
        OidcAuthFlow flow,
        IOptions<OidcOptions> options)
        : base(user, flow)
    {
        var configured = FirstConfigured(options)
            ?? throw new InvalidOperationException("OIDC 直配通道未配置：TKWF:Federation:Oidc 节 Channels 为空");
        _config = configured;
        _channelType = string.IsNullOrWhiteSpace(configured.Platform)
            ? "oidc"
            : $"{configured.Platform}_oidc";
    }

    /// <inheritdoc />
    public override string ChannelType => _channelType;

    /// <inheritdoc />
    protected override OidcPlatformConfig EffectiveConfig => _config;

    /// <inheritdoc />
    protected override OidcPlatformConfig Defaults() => new();   // 完全配置驱动——自托管 IdP 无平台默认端点
}