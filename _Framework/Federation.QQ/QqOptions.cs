using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Federation.QQ;

/// <summary>
/// QQ 互联 channel 配置（单实例——按 <see cref="ChannelId"/> 区分多应用）。
/// <para>⚠️ N1 骨架（Oracle P1-1 定案）：QQ 仅 AppId + AppSecret（AppKey=ClientSecret 同物，
/// 命名对齐 WeChat 先例 AppId+AppSecret）两凭证字段——ChannelId + AppId + AppSecret；
/// <see cref="EnableUnionId"/> 为 unionid 可选增强开关（N3 §3.3——需官网自助申请）。
/// 凭证自持（Oracle P2-4）：生产 AES-GCM 密文或装配注入，永不明文进配置库。</para>
/// </summary>
public class QqChannelConfig
{
    /// <summary>channel 实例 id（<c>ISsoChannel.ChannelId</c> 选区依据）。</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>
    /// 通道对外别名（方案 §3.7 双键——可空，缺省 null = 对外用 <see cref="ChannelId"/>，存量零迁移）。
    /// <para>语义：alias = 对外名（URL 路径段 /{prefix}/{platformId}/oauth/{alias}/callback / 展示 / 客户端入参），与内部
    /// <see cref="ChannelId"/> 一一对应；可读直观 + 保护内部持久化键。解析统一经
    /// <c>IChannelRegistry.GetByAliasOrIdAsync</c>（先 alias → 再 ChannelId）。</para>
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>QQ 互联 AppId（client_id——凭证解析键）。</summary>
    public string AppId { get; set; } = "";

    /// <summary>QQ 互联 AppSecret（AppKey/ClientSecret 同物——生产 AES-GCM 密文或装配注入，永不明文进配置库）。</summary>
    public string AppSecret { get; set; } = "";

    /// <summary>启用 unionid 可选增强（默认 false——需官网自助申请，≤60 应用上限；开启时额外拉取并写联盟锚点辅助，N3 §3.3）。</summary>
    public bool EnableUnionId { get; set; }
}

/// <summary>
/// QQ 互联平台网关库配置选项（<c>TKWF:Federation:QQ</c> 节——Channels 列表按应用区分）。
/// <para>[Options("TKWF:Federation:QQ")]：SG1 在消费方生成 GeneratedOptionsBindings，宿主启动期自动绑定
/// （对齐 WeChatOptions 先例；亦可在消费方 ConfigureExtensions 编程覆盖）。</para>
/// </summary>
[Options("TKWF:Federation:QQ")]
public class QqOptions
{
    /// <summary>channel 配置列表（每项一个 ChannelId/AppId/AppSecret/EnableUnionId）。</summary>
    public List<QqChannelConfig> Channels { get; set; } = [];
}
