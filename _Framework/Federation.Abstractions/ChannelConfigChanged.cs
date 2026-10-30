using System;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道配置变更域事件（D03 §4.2.3 三机制之一「事件失效」——WeChatOperations DB 写路径
/// 落库后发布，缓存层订阅失效命中通道；对齐 <see cref="FollowChangedEvent"/> 形态）。
/// <para>发布点：<c>WeChatOperations/ChannelConfigService</c> SaveChannelAsync/DeleteChannelAsync 落库成功后——
/// 经 <c>User.GetService&lt;ILocalEventBus&gt;()</c> 发布（AOP 帧内收集，UoW 提交后派发——先例同上）。</para>
/// <para><c>DataVersion</c> 语义 = 数据版本快照（<c>Entity.UpdateTime.Ticks</c> 或乐观并发令牌——
/// 区别于 <c>OperationsConfigBase.Version</c> 乐观并发令牌（1.10 运营配置面），Obs5 对齐定稿）——
/// 订阅方可用于乱序检测（版本小于已见 → 忽略）。</para>
/// <para>消费方形态：<c>[DomainEventHandler]</c> 特性 + SG1 编译期注册（框架标准路径）；
/// Federation 主包缓存层（<c>IChannelRegistry</c> 组合实现）订阅后经
/// <c>InvalidateAsync(channelId)</c> 失效对应缓存项。</para>
/// <para>⚠️ <c>ChannelId</c> 为 null = 全量变更（同 <c>InvalidateAsync(null)</c> 全量失效语义）。</para>
/// </summary>
public sealed record ChannelConfigChanged(
    string? ChannelId,
    long DataVersion);