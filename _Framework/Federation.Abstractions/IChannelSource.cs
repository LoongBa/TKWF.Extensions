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

    /// <summary>
    /// 通道能力声明（I2 方案 §3.4 Q5）——<b>DIM 默认 <see cref="ChannelCapabilities.None"/> = 刻意 fail-closed</b>：
    /// 新增平台库忘记声明 → 事件/关注域/免登静默跳过（而非「试着调一下看报不报错」）。既有 8 平台库零改动即可编译。
    /// <para>⚠️ per-source 粗粒度（一个源一个平台）；<b>per-channel 能力</b>（D03 Capability 派生——
    /// DB 源多公众号各自 (ChannelForm, Verification) 不同）经 <see cref="ChannelConfig.Capabilities"/> 承载
    /// （Oracle6 条件 1 方案 A——本 DIM 供静态源投影填充用，DB 源投影时覆盖）。</para>
    /// </summary>
    ChannelCapabilities Capabilities => ChannelCapabilities.None;

    /// <summary>
    /// 组合选区优先级（D03 §4.2.3「优先级配置化」——替代现状硬编码 <c>Insert(0)</c>）。
    /// <b>DIM 默认 0</b>——数值高者优先；DB Provider（WeChatOperations）声明 100，静态源默认 0。
    /// 组合语义 = 按 Priority 降序<b>首命中返回</b>（首个返回非 null 的源即终值——高优先源覆盖低优先源，
    /// Oracle6 条件 5 alias 覆盖语义）。零破坏：既有实现零改动编译。
    /// </summary>
    int Priority => 0;
}