using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.Oidc;

/// <summary>
/// 自托管 IdP 直配通道（OidcConfiguredChannel——国外规划 §三 P2-3：Keycloak/Okta/Auth0/Authentik
/// 经通用基座配置驱动接入，零专属代码）。
/// <para>v0.3.0 多通道联邦：ctor 收 <see cref="ChannelConfig"/>（工厂预取）——配置完全由
/// <c>OidcOptions.Channels</c> 经 <see cref="OidcChannelSource"/> 投影承载（平台字段进 Extra），
/// 本通道零 Options 选区（删 Channels[0] 断点）；<c>ChannelType = "oidc_oidc"</c> 稳定类型
/// （集合模板索引源——工厂按 <see cref="ChannelConfig.PlatformType"/>="oidc" 推导匹配兼容）。</para>
/// <para>注册 <c>AddOidcFederationChannel(name, configure)</c> 时向 <c>OidcOptions.Channels</c> 追加配置，
/// 并注册本具体包装通道（Oracle P0-1 修正：注册具体包装通道而非抽象基类——抽象类 DI 无法实例化运行时必炸）。</para>
/// <para><c>Defaults</c> 返回空（完全配置驱动——自托管 IdP 端点各异无默认）。</para>
/// </summary>
public sealed class OidcConfiguredChannel : OidcChannelBase
{
    /// <summary>构造——channel 由工厂预取（POCO 非域服务）；null = 集合模板实例。</summary>
    public OidcConfiguredChannel(IDomainUser user, OidcChannelFlow flow, ChannelConfig? channel = null)
        : base(user, flow, channel)
    {
    }

    /// <inheritdoc />
    /// <summary>稳定类型 "oidc_oidc"——自托管 IdP 统一走直配包装（平台差异经 Extra["Platform"] 承载，
    /// 不进入 ChannelType；与工厂 <c>IsOAuthChannel("oidc_oidc", "oidc")</c> 推导兼容——登录门面 null 选区路径）。</summary>
    public override string ChannelType => "oidc_oidc";

    /// <inheritdoc />
    protected override OidcPlatformConfig Defaults() => new();   // 完全配置驱动——自托管 IdP 无平台默认端点
}
