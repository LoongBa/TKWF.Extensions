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

    /// <summary>
    /// 通道对外别名（方案 §3.7 双键——可空，缺省 null = 对外用 <see cref="ChannelId"/>，存量零迁移）。
    /// <para>语义：alias = 通道实例的<b>对外名</b>（URL 路径段 / 展示 / 客户端入参），与内部
    /// <see cref="ChannelId"/> 一一对应（同实例双名）；可读直观（merchant-a / edu-course）+ 保护内部持久化键
    /// （外部 URL 暴露 alias 不泄露 ChannelId）。解析入口统一经
    /// <see cref="IChannelRegistry.GetByAliasOrIdAsync"/>（先 alias 精确匹配 → 再 ChannelId 匹配）；
    /// <b>内部消费（通道 / accesscode / 映射）一律用 ChannelId</b>——alias 仅对外入口解析一次归一内部键。</para>
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>平台族字符串（wechat / qq / dingtalk / wecom / oidc / google / microsoft / alipay...开放注册表）。</summary>
    public string PlatformType { get; init; } = "";

    /// <summary>OAuth 握手应用标识（AppId / ClientId 归一——OIDC 系 = client_id）。</summary>
    public string? AppId { get; init; }

    /// <summary>对称应用密钥（AppSecret / ClientSecret 归一；OIDC 系 = client_secret）。静态层可明文（开发态），DB 层密文解密后。</summary>
    public string? AppSecret { get; init; }

    /// <summary>平台特定负载扩展字典（AES-GCM 密文解密后明文——DB 层；静态层直接投影）。</summary>
    public IReadOnlyDictionary<string, string?> Extra { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>默认通道标记（单通道降级（活跃数==1；多通道 B 守卫 CHANNEL_REQUIRED）选区）。</summary>
    public bool IsDefault { get; init; }

    /// <summary>启停标记（false → CHANNEL_DISABLED）。</summary>
    public bool IsEnabled { get; init; } = true;
}