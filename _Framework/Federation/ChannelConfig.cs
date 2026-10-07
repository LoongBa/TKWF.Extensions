using System;
using System.Collections.Generic;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道配置统一模型（多通道联邦 v0.3.0——公共列 + 扩展字典，见 docs/Federation/多通道联邦-开发方案.md §3.1）。
/// <para><b>公共列</b>（各平台 ChannelConfig 的公共核——WeChat/QQ/DingTalk/WeCom/OIDC 系实证归一）：
/// <see cref="ChannelId"/>（选区键）/ <see cref="PlatformType"/>（平台族字符串：wechat/qq/dingtalk/wecom/oidc/google/microsoft——
/// 开放注册表不枚举，支付宝 = 新字符串零 DDL）/ <see cref="AppId"/>（OAuth 握手应用标识）/ <see cref="AppSecret"/>
/// （对称应用密钥；OIDC 系 = ClientSecret）/ <see cref="IsDefault"/> / <see cref="IsEnabled"/>。</para>
/// <para><b>扩展字典 <see cref="Extra"/></b>：平台特定负载（对齐 SsoChannelAuthContext 可扩展键值模式哲学）——
/// WeChat {Token, EncodingAESKey} / DingTalk {CorpId, AppKey} / WeCom {AgentId, IsThirdParty, ...} /
/// OIDC {TokenIssuers, DiscoveryUri, Scopes, Tenant} / 支付宝（未来）{AppPrivateKey(RSA), Environment}。
/// 法人级标识（CorpId/OrgId/AgentId 类）一律进 Extra，不挤占 <see cref="AppId"/>（映射规则约束——方案 M7）。
/// 多环境（沙箱/生产）经 Extra["Environment"] 承载（方案 M11 前瞻——零 DDL）。</para>
/// <para>来源：静态层 = 平台库 <c>XxxOptions.Channels[i]</c> 投影（<see cref="IChannelSource"/>）；
/// DB 层（第二迭代）= <c>SsoChannelRegistryEntity</c> 解密行（AppSecret/Extra 全程密文）。</para>
/// </summary>
public sealed class ChannelConfig
{
    /// <summary>通道实例键（<c>ISsoChannel.ChannelId</c> 选区依据——如公众号 id）。</summary>
    public string ChannelId { get; init; } = "";

    /// <summary>平台族字符串（wechat / qq / dingtalk / wecom / oidc / google / microsoft / alipay...开放注册表）。</summary>
    public string PlatformType { get; init; } = "";

    /// <summary>OAuth 握手应用标识（AppId / ClientId 归一——OIDC 系 = client_id）。</summary>
    public string? AppId { get; init; }

    /// <summary>对称应用密钥（AppSecret / ClientSecret 归一；OIDC 系 = client_secret）。静态层可明文（开发态），DB 层密文解密后。</summary>
    public string? AppSecret { get; init; }

    /// <summary>平台特定负载扩展字典（AES-GCM 密文解密后明文——DB 层；静态层直接投影）。</summary>
    public IReadOnlyDictionary<string, string?> Extra { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>默认通道标记（无前缀 /sso/oauth/callback 降级选区）。</summary>
    public bool IsDefault { get; init; }

    /// <summary>启停标记（false → CHANNEL_DISABLED）。</summary>
    public bool IsEnabled { get; init; } = true;
}