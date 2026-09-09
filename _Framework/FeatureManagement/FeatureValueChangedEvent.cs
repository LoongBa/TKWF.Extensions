using System;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 值变更事件（v0.2.0）——写路径（Set/Delete）Commit 后经 <c>ILocalEventBus</c> 发布，
/// 消费方 <c>[DomainEventHandler]</c> 订阅：审计联动、跨实例失效（接自有总线）等。
/// <para>进程内缓存失效**不依赖本事件**（v0.2.0 版本号方案：写后 version++ 全层即时失效）——
/// 事件仅消费方钩子（评审 P1 裁定）。</para>
/// </summary>
public sealed record FeatureValueChangedEvent(
    string Name,
    string ProviderName,
    string? ProviderKey,
    string? OldValue,
    string? NewValue);
