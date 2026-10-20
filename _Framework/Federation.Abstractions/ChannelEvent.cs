using System;
using System.Collections.Generic;

namespace TKWF.Ext.Federation;

/// <summary>
/// 平台无关的语义事件（协议层解析产物——只描述「发生了什么」，不含回复/不含业务决策）。
/// <para>I2 语义层（方案 §3.3）：平台库通道验签解密后将微信 XML 等原始报文解析为本类型；
/// 关注/取关/扫码/消息/菜单点击等统一表达；编排层消费（记录关注状态 / 派发 handler / 触发域事件）。</para>
/// </summary>
/// <param name="ChannelId">通道实例 id（微信 = 公众号 id）。</param>
/// <param name="ChannelType">通道类型（wechat_event / dingtalk_event——编排按此过滤 handler）。</param>
/// <param name="ExternalUserId">渠道身份标识（微信 = openid；<b>不可空</b>——解析不出时应返回
/// <see cref="ChannelEventOutcome.NotApplicable"/>/<see cref="ChannelEventOutcome.Rejected"/>，而非造半个事件）。</param>
/// <param name="UnionId">跨渠道关联 id（微信 UnionId——可空，未开放时不解析）。</param>
/// <param name="EventType">事件类型（follow | unfollow | scan_follow | scan_only | message | menu_click | ...）。</param>
/// <param name="EventKey">附加键（扫码 scene / 菜单 key——可空）。</param>
/// <param name="Source">来源场景（扫码来源/渠道标识——供统计维度）。</param>
/// <param name="MessageId">消息类事件的 MsgId（事件类为 null）。</param>
/// <param name="OccurredAt">渠道时间戳（UTC 换算）。</param>
/// <param name="ReceivedAt">服务端接收时间（UTC）——排序守卫同秒次判据（I2 §4.3 oracle6 C4：
/// 两段比较 <c>OccurredAt</c> 相等时以 <c>ReceivedAt</c> 决先后）；未显式设置时取构造时刻 UtcNow。</param>
/// <param name="RawPayload">解密后的原始报文（微信 XML 等——诊断/SyncProxy 透传）。</param>
/// <param name="Attributes">协议补充字段（只读 + 键名强制 <c>{platform}.{Name}</c> 前缀——见方案 §3.3 键清单）。</param>
public sealed record ChannelEvent
{
    public required string ChannelId { get; init; }
    public required string ChannelType { get; init; }        // wechat_event / dingtalk_event
    public required string ExternalUserId { get; init; }      // openid / userid
    public string? UnionId { get; init; }
    public required string EventType { get; init; }           // follow|unfollow|scan_follow|scan_only|message|menu_click|...
    public string? EventKey { get; init; }                    // 扫码 scene / 菜单 key
    public string? Source { get; init; }                       // 来源场景（扫码来源/渠道标识——供统计维度）
    public string? MessageId { get; init; }                    // 消息类事件的 MsgId（事件类为 null）
    public DateTime OccurredAt { get; init; }                  // 渠道时间戳 → UTC
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;  // 服务端接收时间（排序守卫同秒次判据）
    public string? RawPayload { get; init; }                   // 解密后的原始报文（微信 XML 等——诊断/SyncProxy 透传）
    public IReadOnlyDictionary<string, string?> Attributes { get; init; } = Empty;

    /// <summary>空属性字典（避免每实例 new——record 缺省引用）。</summary>
    private static readonly IReadOnlyDictionary<string, string?> Empty = new Dictionary<string, string?>();
}

/// <summary>事件解析结果状态（I2 方案 §3.3——用状态而非异常：微信回调必须 200 空回复，5 秒 SLA）。</summary>
public enum ChannelEventOutcomeState
{
    NotApplicable = 0,  // 非事件通道（DIM 默认）——编排跳过
    Parsed        = 1,  // 解析成功，Event 非 null
    Rejected      = 2   // 信任根失败/解密失败——编排短路（不记录、不回复、不回退 handler）
}

/// <summary>事件解析结果（状态 + 语义事件；<b>不携带平台回复</b>——V0.4.0 起回复由 handler 层产出）。</summary>
public sealed record ChannelEventOutcome(ChannelEventOutcomeState State, ChannelEvent? Event)
{
    public static ChannelEventOutcome NotApplicable { get; } = new(ChannelEventOutcomeState.NotApplicable, null);
    public static ChannelEventOutcome Rejected { get; } = new(ChannelEventOutcomeState.Rejected, null);
    public static ChannelEventOutcome Parsed(ChannelEvent evt) => new(ChannelEventOutcomeState.Parsed, evt);
}

/// <summary>handler 输入（编排层产出——语义事件 + 已解析的 UId）。</summary>
/// <param name="Event">语义事件。</param>
/// <param name="UId">编排层经 <c>IExternalIdentityService</c> 只读查询出的平台 UId（未绑定 = null）。</param>
public sealed record ChannelEventDispatch(ChannelEvent Event, string? UId);