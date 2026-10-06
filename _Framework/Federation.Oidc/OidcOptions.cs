using System.Collections.Generic;

namespace TKWF.Federation.Oidc;

/// <summary>
/// OIDC 通道基座配置选项（<c>TKWF:Federation:Oidc</c> 节——Channels 列表，每项一个 OidcPlatformConfig）。
/// <para>[Options("TKWF:Federation:Oidc")]：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定
/// （对齐 WeChatOptions/FederationOptions 先例）；自托管 IdP 直配场景亦可在编程配置覆盖。</para>
/// </summary>
public sealed class OidcOptions
{
    /// <summary>OIDC 平台配置列表（多 IdP——每个 ChannelId 一个配置）。</summary>
    public List<OidcPlatformConfig> Channels { get; set; } = [];
}