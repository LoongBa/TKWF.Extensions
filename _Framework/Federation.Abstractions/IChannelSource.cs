using System.Collections.Generic;

namespace TKWF.Ext.Federation;

/// <summary>
/// 静态通道来源（多通道联邦 v0.3.0——平台库把自己的 <c>XxxOptions.Channels</c> 投影为统一 <see cref="ChannelConfig"/>）。
/// <para>各平台库实现本接口：在 <c>AddXxxFederationChannels()</c> 内注册，把平台强类型配置
/// （WeChatChannelConfig/QQChannelConfig...）翻译为统一模型（公共列归一 + 扩展字典承载平台特有字段）
/// ——平台库凭证自持（Oracle P2-4）不失效，只是投影形态统一。</para>
/// <para>注册：普通 DI 集合（<c>TryAddEnumerable</c>——非 IDomainService，无守卫工厂语义）；
/// <see cref="StaticChannelRegistry"/> 聚合全部来源。DB 层（第二迭代）由
/// <c>SsoChannelRegistryEntity</c> 承载，不实现本接口。</para>
/// </summary>
public interface IChannelSource
{
    /// <summary>平台族（wechat / qq / dingtalk / wecom / oidc / google / microsoft...——与 ChannelConfig.PlatformType 对齐）。</summary>
    string PlatformType { get; }

    /// <summary>把平台配置 Channels 投影为统一配置列表（静态层——每次调用重新读 Options，反映配置热更新）。</summary>
    IReadOnlyList<ChannelConfig> GetChannels();
}