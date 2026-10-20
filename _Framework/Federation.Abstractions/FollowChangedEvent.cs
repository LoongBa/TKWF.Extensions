using System;

namespace TKWF.Ext.Federation;

/// <summary>
/// 关注关系变化域事件（I2 渠道关注关系域 §3.7——仅 <c>StateChanged == true</c> 时发布；
/// 重复投递/乱序回落不产生噪音）。
/// <para>发布点：编排层 <see cref="IEventOrchestrator"/>（§5.1 时序第 6 步记录在后 + 第 7 步发布）——
/// <c>RecordAsync</c> 返回 <see cref="FollowRecordResult.StateChanged"/>，编排层持 <c>UId</c>（经
/// <c>IExternalIdentityService.GetByExternalIdAsync</c> 解析）组装本事件经
/// <c>User.GetService&lt;ILocalEventBus&gt;().PublishAsync(evt)</c> 发布（AOP 帧内收集，UoW 提交后派发——
/// oracle6 C1：<c>ILocalEventBus</c> 非 <c>IDomainService</c>，须用 <c>GetService</c> 非 <c>Use</c>）。</para>
/// <para><c>UId</c> 未绑定（null）时照常发布——业务层自行决定「未绑定用户是否忽略」；
/// 消费方形态：<c>[DomainEventHandler]</c> 特性 + SG1 编译期注册（框架标准路径，零新依赖）。</para>
/// </summary>
public sealed record FollowChangedEvent(
    string ChannelId,
    string ChannelType,
    string ExternalUserId,
    string? UnionId,
    string? UId,
    bool IsFollowing,
    string EventType,
    string? EventKey,
    string? Source,
    DateTime OccurredAt);
