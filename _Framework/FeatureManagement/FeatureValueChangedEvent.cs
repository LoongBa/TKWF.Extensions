using System;
using TKW.Framework.Domain.Events;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 值变更事件（v0.2.0 引入；v0.3.0 标注 <see cref="DistributedEventAttribute"/>）——写路径（Set/Delete）Commit 后
/// 经 <c>ILocalEventBus</c> 发布；生产 AOP 链内 <c>EventDispatchFilter.PostProceed</c> 检测 <see cref="DistributedEventAttribute"/>
/// 自动路由到 <c>IDistributedEventBus</c>（非 LocalDistributedEventBus 时走真实传输）——跨实例缓存失效 + 外部服务联动。
/// <para>纯 string 字段 record（EVT004 denylist Delegate/Action/Func/Stream/CancellationToken/Task 均不命中——合规）；
/// 事件类型统一——本地/分布式双通道同 payload，消费方零认知负担。</para>
/// <para>进程内缓存失效**不依赖本事件**（v0.2.0 版本号方案：写后 version++ 全层即时失效）——事件仅消费方钩子 +
/// 跨实例失效（内建 <see cref="DistributedFeatureChangedHandler"/> 消费 bump 版本表）。</para>
/// </summary>
[DistributedEvent]
public sealed record FeatureValueChangedEvent(
    string Name,
    string ProviderName,
    string? ProviderKey,
    string? OldValue,
    string? NewValue);
