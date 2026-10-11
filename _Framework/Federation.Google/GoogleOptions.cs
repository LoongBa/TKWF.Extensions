using System.Collections.Generic;

namespace TKWF.Federation.Google;

/// <summary>
/// Google 通道配置（单实例——M2 方案 §3.3 Oracle P1-3：**一个消费方只配一个 Google channel 实例**，
/// 多 Google App 场景 channel_id 加 client_id 区分但映射表按 sub 归一是 N2 §3.3 public 通配语义边界，
/// 本库不实现多 App 场景）。凭证自持（Oracle P2-4）。
/// </summary>
public sealed class GoogleChannelConfig
{
    /// <summary>channel 实例 id（<c>ISsoChannel.ChannelId</c> 选区——public 平台经 BuildChannelId 通配 `google_oidc:*`）。</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>
    /// 通道对外别名（方案 §3.7 双键——可空，缺省 null = 对外用 <see cref="ChannelId"/>，存量零迁移）。
    /// <para>语义：alias = 对外名（URL 路径段 /{prefix}/{platformId}/oauth/{alias}/callback / 展示 / 客户端入参），与内部
    /// <see cref="ChannelId"/> 一一对应；可读直观 + 保护内部持久化键。解析统一经
    /// <c>IChannelRegistry.GetByAliasOrIdAsync</c>（先 alias → 再 ChannelId）。</para>
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>Google OAuth client_id（Google 仅两凭证——Oracle P1-1 对齐 WeChat 先例）。</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Google OAuth client_secret（生产 AES-GCM 密文或装配注入，永不明文进配置库）。</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>TokenIssuer 白名单（默认 accounts.google.com；支持通配/正则——M1 issuer 容错）。</summary>
    public List<string> TokenIssuers { get; set; } = [];
}

/// <summary>
/// Google 平台网关库配置选项（<c>TKWF:Federation:Google</c> 节——Channels 列表，单实例语义）。
/// <para>[Options("TKWF:Federation:Google")]：SG1 在消费方生成 GeneratedOptionsBindings 自动绑定（对齐先例）。</para>
/// </summary>
public sealed class GoogleOptions
{
    /// <summary>channel 配置列表（单实例——Channels[0] 选区）。</summary>
    public List<GoogleChannelConfig> Channels { get; set; } = [];
}