using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 关注域门面实现（I2 渠道关注关系域 §3.6——状态聚合根更新 + 事件事实追加写）。
/// <para>数据访问红线：零 Store 伪层/零 IEntityDAC/零 ORM 注入——全部经 SG1 生成 DataService 内部转发访问器
/// （<c>EntityGetAsync/EntitySelectAsync/EntityCreateAsync/EntityUpdateAsync/EntityUpdateWhereAsync/EntityDeleteBatchAsync</c>），
/// DataService 经基类 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载。IDomainUser 永不注册 DI（经基类 <c>User</c>）。</para>
/// <para>幂等（§4.3）：<see cref="RecordAsync"/> 先按 <see cref="FollowEventEntity.DedupeKey"/> 预查存在（常规路径零异常）；
/// 插入仍冲突（真并发重投）→ 捕获唯一约束异常 → <c>Recorded=false</c> 返回（不二次写）。</para>
/// <para>排序守卫（§4.3 oracle6 C4）：两段比较 <c>newer</c> 判定（见 <see cref="IsNewer"/>）——与条件更新
/// <see cref="FollowStateEntityDataService.EntityUpdateWhereAsync"/> 的 LINQ Where 同型；仅 newer 时覆盖状态字段。</para>
/// </summary>
[DiContractIgnore]
internal sealed class FollowStateService : DomainServiceBase, IFollowStateService
{
    private readonly ILogger<FollowStateService> _logger;
    private FollowEventEntityDataService? _events;
    private FollowStateEntityDataService? _states;

    private FollowEventEntityDataService Events => _events ??= User.Use<FollowEventEntityDataService>();
    private FollowStateEntityDataService States => _states ??= User.Use<FollowStateEntityDataService>();

    /// <summary>构造——IDomainUser 经守卫工厂帧内供给（AddConstructibleService）。</summary>
    public FollowStateService(IDomainUser user, ILogger<FollowStateService> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<FollowRecordResult> RecordAsync(ChannelEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (string.IsNullOrWhiteSpace(evt.ChannelId) || string.IsNullOrWhiteSpace(evt.ExternalUserId))
            throw new ArgumentException("ChannelEvent 缺 ChannelId/ExternalUserId——无法记录关注域", nameof(evt));

        // ① 事件事实追加写（幂等——DedupeKey 唯一约束兜底；事实不可丢）
        var eventEntity = ToFollowEvent(evt);
        var recorded = await TryInsertEventAsync(eventEntity, ct);

        // ② 状态聚合根更新（仅关注域事件改变状态；message/menu_click 等只记事实不翻状态——
        //    消除「未关注却因消息建状态行」的二义性）
        FollowStateDto? stateDto = null;
        var stateChanged = false;
        if (IsFollowTransition(evt.EventType))
        {
            var state = await States.EntityGetAsync(
                s => s.ChannelId == evt.ChannelId && s.ExternalUserId == evt.ExternalUserId, ct);
            if (state is null)
            {
                // 无状态行 → 建行（新用户首次关注/取关）
                await States.EntityCreateAsync(ToFollowState(evt), ct);
                stateDto = ToDto(await States.EntityGetAsync(
                    s => s.ChannelId == evt.ChannelId && s.ExternalUserId == evt.ExternalUserId, ct));
                stateChanged = true;
            }
            else if (IsNewer(evt, state))
            {
                // 新事件（排序守卫通过）→ 覆盖状态字段
                ApplyEvent(state, evt);
                state.UpdateTime = DateTime.UtcNow;
                await States.EntityUpdateAsync(state, ct);
                stateDto = ToDto(state);
                stateChanged = true;
            }
            else
            {
                // 旧事件（乱序晚到）→ 状态不动（事件行已落库）
                _logger.LogDebug("关注状态排序守卫挡下旧事件：ChannelId={ChannelId} ExternalUserId={ExternalUserId} OccurredAt={OccurredAt:O}",
                    evt.ChannelId, evt.ExternalUserId, evt.OccurredAt);
                stateDto = ToDto(state);
            }
        }

        return new FollowRecordResult(recorded, stateChanged, stateDto);
    }

    /// <inheritdoc />
    public async Task<FollowStateDto?> GetAsync(string channelId, string externalUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(externalUserId)) return null;
        var state = await States.EntityGetAsync(
            s => s.ChannelId == channelId && s.ExternalUserId == externalUserId, ct);
        return ToDto(state);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FollowStateDto>> GetByExternalUserIdAsync(string externalUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalUserId)) return [];
        var list = await States.EntitySelectAsync(s => s.ExternalUserId == externalUserId, ct: ct);
        return list.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FollowDailyStatDto>> GetDailyStatsAsync(
        string channelId, DateTime fromUtc, DateTime toUtc, string? source = null, CancellationToken ct = default)
    {
        // 事件事实区间查询 → 内存按日分桶（日期段通常窗口有限——量级可控；SQL 日期分桶留框架能力迭代）
        var events = await Events.EntitySelectAsync(e =>
            e.ChannelId == channelId
            && e.OccurredAt >= fromUtc && e.OccurredAt < toUtc
            && (source == null || e.Source == source), ct: ct);

        return events
            .GroupBy(e => e.OccurredAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new FollowDailyStatDto(
                Date: g.Key,
                NewFollows: g.Count(e => IsFollow(e.EventType)),
                Unsubscribes: g.Count(e => e.EventType == "unfollow"),
                NetGrowth: g.Count(e => IsFollow(e.EventType)) - g.Count(e => e.EventType == "unfollow"),
                Source: source))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<int> RebuildAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return 0;
        var events = await Events.EntitySelectAsync(e => e.ChannelId == channelId, ct: ct);
        // 按用户分组 → OccuredAt 排序 → 重放状态（事件行已是事实，按 ReceivedAt 同秒次判据稳定）
        var groups = events
            .GroupBy(e => e.ExternalUserId)
            .OrderByDescending(g => g.Max(e => e.OccurredAt));

        var corrected = 0;
        foreach (var group in groups)
        {
            var state = await States.EntityGetAsync(
                s => s.ChannelId == channelId && s.ExternalUserId == group.Key, ct);
            if (state is null)
            {
                await States.EntityCreateAsync(ReplayState(channelId, group.Key, group), ct);
                corrected++;
            }
            else
            {
                var expect = ToDto(ReplayState(channelId, group.Key, group));
                if (state.IsFollowing != expect.IsFollowing
                    || state.SubscribedAt != expect.SubscribedAt
                    || state.UnsubscribedAt != expect.UnsubscribedAt)
                {
                    ApplyReplay(state, group);
                    state.UpdateTime = DateTime.UtcNow;
                    await States.EntityUpdateAsync(state, ct);
                    corrected++;
                }
            }
        }
        return corrected;
    }

    /// <inheritdoc />
    public async Task<int> PurgeEventsAsync(DateTime beforeUtc, int batchSize = 500, CancellationToken ct = default)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var expired = await Events.EntitySelectAsync(e => e.OccurredAt < beforeUtc, 0, batchSize, ct: ct);
        if (expired.Count == 0) return 0;
        return await Events.EntityDeleteBatchAsync(expired.Select(e => e.Id).ToList(), ct);
    }

    // ── 私有辅助 ──

    /// <summary>关注域事件（翻状态的语义）——follow / scan_follow（含扫码关注）；unfollow 独立处理。</summary>
    private static bool IsFollowTransition(string eventType)
        => eventType is "follow" or "unfollow" or "scan_follow";

    private static bool IsFollow(string eventType)
        => eventType is "follow" or "scan_follow";

    /// <summary>排序守卫（oracle6 C4——两段比较，不可用元组 &gt;；与 DataService 条件更新 Where 同型）。</summary>
    private static bool IsNewer(ChannelEvent evt, FollowStateEntity state)
        => evt.OccurredAt > state.LastEventAt
           || (evt.OccurredAt == state.LastEventAt && evt.ReceivedAt > state.LastReceivedAt);

    /// <summary>ChannelEvent → FollowEventEntity（DedupeKey 构造：超 256 字符 SHA256）。</summary>
    private static FollowEventEntity ToFollowEvent(ChannelEvent evt)
    {
        var receivedAt = evt.ReceivedAt == default ? DateTime.UtcNow : evt.ReceivedAt;
        return new FollowEventEntity
        {
            ChannelId = evt.ChannelId,
            ExternalUserId = evt.ExternalUserId,
            UnionId = evt.UnionId,
            EventType = evt.EventType,
            EventKey = evt.EventKey,
            Source = evt.Source,
            MessageId = evt.MessageId,
            OccurredAt = evt.OccurredAt == default ? receivedAt : evt.OccurredAt,
            ReceivedAt = receivedAt,
            DedupeKey = BuildDedupeKey(evt, receivedAt),
            CreatedAt = receivedAt,
        };
    }

    /// <summary>幂等键：<c>{channelId}|{eventType}|{externalUserId}|{occurredAtUnix}|{eventKey}|{messageId}</c>
    /// ——超 256 字符 SHA256 十六进制（保留可判定性，截断会产生假碰撞，§4.3）。</summary>
    internal static string BuildDedupeKey(ChannelEvent evt, DateTime receivedAtUtc)
    {
        var occurredAtUnix = new DateTimeOffset(evt.OccurredAt == default ? receivedAtUtc : evt.OccurredAt, TimeSpan.Zero).ToUnixTimeSeconds();
        var raw = $"{evt.ChannelId}|{evt.EventType}|{evt.ExternalUserId}|{occurredAtUnix}|{evt.EventKey ?? "-"}|{evt.MessageId ?? "-"}";
        if (raw.Length <= 256) return raw;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    /// <summary>初始状态行（新用户首事件）。</summary>
    private static FollowStateEntity ToFollowState(ChannelEvent evt)
    {
        var receivedAt = evt.ReceivedAt == default ? DateTime.UtcNow : evt.ReceivedAt;
        var isFollowing = IsFollow(evt.EventType);
        var state = new FollowStateEntity
        {
            ChannelId = evt.ChannelId,
            ExternalUserId = evt.ExternalUserId,
            UnionId = evt.UnionId,
            IsFollowing = isFollowing,
            LastEventType = evt.EventType,
            LastEventKey = evt.EventKey,
            LastSource = evt.Source,
            LastEventAt = evt.OccurredAt,
            LastReceivedAt = receivedAt,
            CreatedAt = receivedAt,
            UpdateTime = receivedAt,
        };
        if (isFollowing) state.SubscribedAt = evt.OccurredAt;
        else state.UnsubscribedAt = evt.OccurredAt;
        return state;
    }

    /// <summary>排序守卫通过后覆盖状态字段（事件时间线前进）。</summary>
    private static void ApplyEvent(FollowStateEntity state, ChannelEvent evt)
    {
        var isFollowing = IsFollow(evt.EventType);
        state.IsFollowing = isFollowing;
        state.UnionId = evt.UnionId ?? state.UnionId;
        state.LastEventType = evt.EventType;
        state.LastEventKey = evt.EventKey;
        state.LastSource = evt.Source;
        state.LastEventAt = evt.OccurredAt;
        state.LastReceivedAt = evt.ReceivedAt == default ? DateTime.UtcNow : evt.ReceivedAt;
        if (isFollowing) state.SubscribedAt = evt.OccurredAt;
        else state.UnsubscribedAt = evt.OccurredAt;
    }

    /// <summary>重放状态（Rebuild——按组内事件时间线推进）。</summary>
    private static FollowStateEntity ReplayState(string channelId, string externalUserId, IEnumerable<FollowEventEntity> events)
    {
        var state = new FollowStateEntity
        {
            ChannelId = channelId,
            ExternalUserId = externalUserId,
            CreatedAt = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        };
        foreach (var e in events.OrderBy(e => e.OccurredAt).ThenBy(e => e.ReceivedAt))
        {
            ApplyEvent(state, new ChannelEvent
            {
                ChannelId = e.ChannelId,
                ChannelType = "wechat_event",
                ExternalUserId = e.ExternalUserId,
                UnionId = e.UnionId,
                EventType = e.EventType,
                EventKey = e.EventKey,
                Source = e.Source,
                OccurredAt = e.OccurredAt,
                ReceivedAt = e.ReceivedAt,
            });
        }
        return state;
    }

    private static void ApplyReplay(FollowStateEntity state, IEnumerable<FollowEventEntity> events)
    {
        foreach (var e in events.OrderBy(e => e.OccurredAt).ThenBy(e => e.ReceivedAt))
        {
            state.IsFollowing = IsFollow(e.EventType);
            state.UnionId = e.UnionId ?? state.UnionId;
            state.LastEventType = e.EventType;
            state.LastEventKey = e.EventKey;
            state.LastSource = e.Source;
            state.LastEventAt = e.OccurredAt;
            state.LastReceivedAt = e.ReceivedAt;
            if (IsFollow(e.EventType)) state.SubscribedAt = e.OccurredAt;
            else state.UnsubscribedAt = e.OccurredAt;
        }
    }

    /// <summary>幂等插入（预查 + 唯一约束兜底——真并发冲突即返回 false，不二次写）。</summary>
    private async Task<bool> TryInsertEventAsync(FollowEventEntity entity, CancellationToken ct)
    {
        var existing = await Events.EntityGetAsync(e => e.DedupeKey == entity.DedupeKey, ct);
        if (existing is not null) return false;
        try
        {
            await Events.EntityCreateAsync(entity, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 唯一约束冲突（真并发重投）——幂等语义：不记录（Recorded=false），不抛（业务照常）
            _logger.LogWarning("关注事件幂等冲突（并发重投）：{DedupeKey}——按未记录处理", entity.DedupeKey);
            return false;
        }
    }

    private static FollowStateDto? ToDto(FollowStateEntity? s)
        => s is null ? null : new FollowStateDto(
            s.ChannelId, s.ExternalUserId, s.UnionId, s.IsFollowing,
            s.SubscribedAt, s.UnsubscribedAt, s.LastEventType, s.LastEventKey, s.LastEventAt);
}