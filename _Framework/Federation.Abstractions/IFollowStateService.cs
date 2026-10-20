using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>关注关系记录结果。</summary>
/// <param name="Recorded">是否首次记录事件事实（幂等重放 = false）。</param>
/// <param name="StateChanged">状态是否被更新（排序守卫挡下旧事件 = false）。</param>
/// <param name="State">更新后的状态（含 null——仅当状态行不存在且创建失败等边缘）。</param>
public sealed record FollowRecordResult(bool Recorded, bool StateChanged, FollowStateDto? State);

/// <summary>关注关系状态（聚合根投影——「当前是否关注」+ 最近事件时间线）。</summary>
public sealed record FollowStateDto(
    string ChannelId,
    string ExternalUserId,
    string? UnionId,
    bool IsFollowing,
    DateTime? SubscribedAt,
    DateTime? UnsubscribedAt,
    string LastEventType,
    string? LastEventKey,
    DateTime LastEventAt);

/// <summary>日粒度统计点（派生消费方：WeChatOperations 订阅统计——Date/NewFollows/Unsubscribes/NetGrowth/Source）。</summary>
public sealed record FollowDailyStatDto(DateTime Date, int NewFollows, int Unsubscribes, int NetGrowth, string? Source);

/// <summary>
/// 关注域门面（I2 渠道关注关系域 §3.6——状态唯一权威 + 事件事实追加写）。
/// <para>实现：<c>FollowStateService : DomainServiceBase</c>（Federation 主包）——经
/// <c>User.Use&lt;FollowEventEntityDataService&gt;()</c> / <c>User.Use&lt;FollowStateEntityDataService&gt;()</c>
/// 组合 SG1 生成的 DataService（<b>零手写 Store</b>——AGENTS §8 红线）；注册
/// <c>AddConstructibleService&lt;IFollowStateService, FollowStateService&gt;()</c>。</para>
/// <para>幂等（§4.3）：<c>DedupeKey</c> = <c>{channelId}|{eventType}|{externalUserId}|{occurredAtUnix}|{eventKey}|{messageId}</c>
/// （超 256 字符 SHA256）；重复推送必然同键 → <see cref="RecordAsync"/> 返回 <c>Recorded=false</c>。</para>
/// <para>排序守卫（§4.3 oracle6 C4）：<c>newer = evt.OccurredAt &gt; state.LastEventAt || (== &amp;&amp; evt.ReceivedAt &gt; state.LastReceivedAt)</c>
/// ——不可用元组 &gt; 运算符（ValueTuple 无定义且不可翻译 SQL），必须两段比较；仅 newer 时覆盖状态字段，
/// 事件行无论是否 newer 都落库（事实不可丢）。</para>
/// </summary>
public interface IFollowStateService : IDomainService
{
    /// <summary>记录事件事实 + 更新状态（幂等）。返回是否首次记录、状态是否变化。</summary>
    Task<FollowRecordResult> RecordAsync(ChannelEvent evt, CancellationToken ct = default);

    /// <summary>单渠道状态查询。</summary>
    Task<FollowStateDto?> GetAsync(string channelId, string externalUserId, CancellationToken ct = default);

    /// <summary>跨渠道查询（业务问「这个人在任意渠道还关注着吗」）。</summary>
    Task<IReadOnlyList<FollowStateDto>> GetByExternalUserIdAsync(string externalUserId, CancellationToken ct = default);

    /// <summary>日粒度统计（派生消费方：WeChatOperations 订阅统计——DATE(OccurredAt) 分桶 + follow/unfollow 计数）。</summary>
    Task<IReadOnlyList<FollowDailyStatDto>> GetDailyStatsAsync(
        string channelId, DateTime fromUtc, DateTime toUtc, string? source = null, CancellationToken ct = default);

    /// <summary>从事件日志重建状态（对账用，不发布事件）。返回修正行数。</summary>
    Task<int> RebuildAsync(string channelId, CancellationToken ct = default);

    /// <summary>保留期清理（消费方定时任务驱动——本扩展不硬依赖 BackgroundJobs）。返回删除行数。</summary>
    Task<int> PurgeEventsAsync(DateTime beforeUtc, int batchSize = 500, CancellationToken ct = default);
}