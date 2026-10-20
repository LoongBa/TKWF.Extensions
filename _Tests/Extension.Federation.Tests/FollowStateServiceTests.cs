using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// I2 关注域门面测试（方案 §10 测试矩阵 I2-5）——<see cref="IFollowStateService"/> 幂等/排序守卫/统计/重建/清理。
/// <para>生产路径宿主模式（对齐 SsoChannelRegistryTests）：真实 DI（FederationExtensionInitializer.ConfigureServices +
/// FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> +
/// <c>User.Use&lt;IFollowStateService&gt;()</c>（AOP 路径——设 CurrentAopUser → GetRequiredService → 守卫工厂）。</para>
/// <para>实体断言经 <c>User.Use&lt;FollowEventEntityDataService&gt;()</c>/<c>FollowStateEntityDataService</c>
/// NoAop 直建（数据访问红线合规——零 IFreeSql/IEntityDAC 直注入）。</para>
/// <para>时间戳约定（SQLite 往返）：用 <see cref="DateTimeKind.Unspecified"/>（对齐 Metrics/Calendar 先例——
/// SQLite 方言存本地时间，Utc Kind 写入往返变 +8 偏移破坏排序守卫两段比较）；语义即 UTC 值。</para>
/// </summary>
public sealed class FollowStateServiceTests : TestHostBase
{
    protected override TestUserInfo CreateUser() => new("follow-user", "关注域测试用户");

    protected override void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<FollowEventEntity>();
        fsql.CodeFirst.SyncStructure<FollowStateEntity>();
    }

    protected override void ConfigureHost(IServiceCollection services)
    {
        new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        FederationTestHost.RegisterEntityDacs(services);
    }

    private static DateTime Utc(DateTime value) => value;

    private static ChannelEvent Follow(string openid, DateTime occurredAt, string? scene = null, string channelId = "mp-1")
        => new()
        {
            ChannelId = channelId,
            ChannelType = "wechat_event",
            ExternalUserId = openid,
            EventType = "follow",
            EventKey = scene,
            Source = scene,
            OccurredAt = occurredAt,
            ReceivedAt = occurredAt.AddSeconds(1),
        };

    private static ChannelEvent Unfollow(string openid, DateTime occurredAt, string channelId = "mp-1")
        => new()
        {
            ChannelId = channelId,
            ChannelType = "wechat_event",
            ExternalUserId = openid,
            EventType = "unfollow",
            OccurredAt = occurredAt,
            ReceivedAt = occurredAt.AddSeconds(1),
        };

    private static ChannelEvent ScanFollow(string openid, DateTime occurredAt, string scene, string channelId = "mp-1")
        => new()
        {
            ChannelId = channelId,
            ChannelType = "wechat_event",
            ExternalUserId = openid,
            EventType = "scan_follow",
            EventKey = scene,
            Source = scene,
            OccurredAt = occurredAt,
            ReceivedAt = occurredAt.AddSeconds(1),
        };

    // ── 1. 幂等（§4.3：重复推送必然同键 → Recorded=false 不二次写）──

    /// <summary>首次关注：Recorded=true + StateChanged=true + IsFollowing=true + SubscribedAt=OccurredAt（生产路径）。</summary>
    [Fact]
    public async Task RecordAsync_FirstFollow_RecordsEventAndCreatesState()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var occurred = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        var result = await user.Use<IFollowStateService>().RecordAsync(Follow("openid-1", occurred), CancellationToken.None);

        Assert.True(result.Recorded);
        Assert.True(result.StateChanged);
        Assert.NotNull(result.State);
        Assert.True(result.State!.IsFollowing);
        Assert.Equal(occurred, result.State.SubscribedAt);
        Assert.Null(result.State.UnsubscribedAt);
        Assert.Equal("follow", result.State.LastEventType);
        Assert.Equal(occurred, result.State.LastEventAt);

        // 事件行落库（事实）
        var events = await user.Use<FollowEventEntityDataService>()
            .EntitySelectAsync(e => e.ExternalUserId == "openid-1", ct: CancellationToken.None);
        Assert.Single(events);
        Assert.Equal(occurred, events[0].OccurredAt);
    }

    /// <summary>重复推送（同键）：Recorded=false + StateChanged=false + 不二次写事件行（§4.3 幂等）。</summary>
    [Fact]
    public async Task RecordAsync_DuplicatePush_Idempotent_NoSecondWrite()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var occurred = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        var first = await user.Use<IFollowStateService>().RecordAsync(Follow("openid-2", occurred), CancellationToken.None);
        var second = await user.Use<IFollowStateService>().RecordAsync(Follow("openid-2", occurred), CancellationToken.None);

        Assert.True(first.Recorded);
        Assert.False(second.Recorded);
        Assert.False(second.StateChanged);

        var events = await user.Use<FollowEventEntityDataService>()
            .EntitySelectAsync(e => e.ExternalUserId == "openid-2", ct: CancellationToken.None);
        Assert.Single(events);
    }

    /// <summary>scan_follow 计入关注（扫码来源维度 R5——Source 落库供统计）。</summary>
    [Fact]
    public async Task RecordAsync_ScanFollow_RecordsWithSource()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var occurred = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        var result = await user.Use<IFollowStateService>()
            .RecordAsync(ScanFollow("openid-3", occurred, "qr_scene_42"), CancellationToken.None);

        Assert.True(result.Recorded);
        Assert.True(result.StateChanged);
        Assert.True(result.State!.IsFollowing);
        Assert.Equal("scan_follow", result.State.LastEventType);

        var evt = await user.Use<FollowEventEntityDataService>()
            .EntityGetAsync(e => e.ExternalUserId == "openid-3", CancellationToken.None);
        Assert.Equal("scan_follow", evt!.EventType);
        Assert.Equal("qr_scene_42", evt.Source);
        Assert.Equal("qr_scene_42", evt.EventKey);

        // 状态行 LastSource 经实体断言（DTO 契约按方案 §3.6 定稿不暴露 LastSource）
        var stateRow = await user.Use<FollowStateEntityDataService>()
            .EntityGetAsync(s => s.ExternalUserId == "openid-3", CancellationToken.None);
        Assert.Equal("qr_scene_42", stateRow!.LastSource);
    }

    // ── 2. 取关（unfollow）──

    /// <summary>关注 → 取关：IsFollowing=false + UnsubscribedAt=取关时间（时间线推进）。</summary>
    [Fact]
    public async Task RecordAsync_FollowThenUnfollow_FlipsState()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var t1 = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);
        var t2 = new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Unspecified);

        await user.Use<IFollowStateService>().RecordAsync(Follow("openid-4", t1), CancellationToken.None);
        var result = await user.Use<IFollowStateService>().RecordAsync(Unfollow("openid-4", t2), CancellationToken.None);

        Assert.True(result.StateChanged);
        Assert.False(result.State!.IsFollowing);
        Assert.Equal(t1, result.State.SubscribedAt);
        Assert.Equal(t2, result.State.UnsubscribedAt);
        Assert.Equal("unfollow", result.State.LastEventType);
        Assert.Equal(t2, result.State.LastEventAt);

        var events = await user.Use<FollowEventEntityDataService>()
            .EntitySelectAsync(e => e.ExternalUserId == "openid-4", ct: CancellationToken.None);
        Assert.Equal(2, events.Count);
    }

    /// <summary>取关后可再关注（重复关注周期）。</summary>
    [Fact]
    public async Task RecordAsync_UnfollowThenFollow_ReSubscribes()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var t1 = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);
        var t2 = new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Unspecified);
        var t3 = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Unspecified);

        await user.Use<IFollowStateService>().RecordAsync(Follow("openid-5", t1), CancellationToken.None);
        await user.Use<IFollowStateService>().RecordAsync(Unfollow("openid-5", t2), CancellationToken.None);
        var result = await user.Use<IFollowStateService>().RecordAsync(Follow("openid-5", t3), CancellationToken.None);

        Assert.True(result.StateChanged);
        Assert.True(result.State!.IsFollowing);
        Assert.Equal(t3, result.State.SubscribedAt);
        Assert.Equal(t2, result.State.UnsubscribedAt);
        Assert.Equal(t3, result.State.LastEventAt);
    }

    // ── 3. 排序守卫（§4.3 oracle6 C4——两段比较）──

    /// <summary>乱序晚到旧事件：事件行落库（事实不可丢）+ 状态不动（StateChanged=false——守卫挡下）。</summary>
    [Fact]
    public async Task RecordAsync_OutOfOrderOldEvent_RecordsFactButKeepsState()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var later = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Unspecified);
        var earlier = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        // 先记录较新事件（follow@later）
        var first = await user.Use<IFollowStateService>().RecordAsync(Follow("openid-6", later), CancellationToken.None);
        Assert.True(first.StateChanged);
        Assert.True(first.State!.IsFollowing);

        // 乱序晚到较旧取关事件（unfollow@earlier）——状态应保持关注（守卫挡下）
        var stale = await user.Use<IFollowStateService>().RecordAsync(Unfollow("openid-6", earlier), CancellationToken.None);

        Assert.True(stale.Recorded);          // 事件事实照记
        Assert.False(stale.StateChanged);     // 状态不被旧事件覆盖
        Assert.NotNull(stale.State);
        Assert.True(stale.State!.IsFollowing); // 仍是关注

        // 事件行 2 条（事实不可丢）
        var events = await user.Use<FollowEventEntityDataService>()
            .EntitySelectAsync(e => e.ExternalUserId == "openid-6", ct: CancellationToken.None);
        Assert.Equal(2, events.Count);

        // 状态行未被动过（LastEventAt 仍是较新事件）
        var state = await user.Use<FollowStateEntityDataService>()
            .EntityGetAsync(s => s.ExternalUserId == "openid-6", CancellationToken.None);
        Assert.Equal("follow", state!.LastEventType);
        Assert.Equal(later, state.LastEventAt);
    }

    /// <summary>同秒并列（OccurredAt 相等）→ ReceivedAt 次判据决先后：较后接收的事件胜出（两段比较必要）。</summary>
    [Fact]
    public async Task RecordAsync_SameOccurredAt_ReceivedAtTiebreak()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var same = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        // 同秒 follow（ReceivedAt=08:00:01）→ 同秒 unfollow（ReceivedAt=08:00:02）——unfollow 胜出
        var follow = Follow("openid-7", same);
        follow = follow with { ReceivedAt = same.AddSeconds(1) };
        var unfollow = Unfollow("openid-7", same);
        unfollow = unfollow with { ReceivedAt = same.AddSeconds(2) };

        var first = await user.Use<IFollowStateService>().RecordAsync(follow, CancellationToken.None);
        var second = await user.Use<IFollowStateService>().RecordAsync(unfollow, CancellationToken.None);

        Assert.True(first.Recorded);
        Assert.True(second.Recorded);
        Assert.True(second.StateChanged);  // ReceivedAt 更晚 → 状态被更新
        Assert.False(second.State!.IsFollowing);
        Assert.Equal("unfollow", second.State.LastEventType);
    }

    /// <summary>乱序同秒旧事件（OccurredAt 相等但 ReceivedAt 更早）→ 守卫挡下状态不变（次判据反向）。</summary>
    [Fact]
    public async Task RecordAsync_SameOccurredAt_OlderReceivedAt_GuardBlocks()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var same = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        // 先 follow（ReceivedAt=08:00:02）→ 后到 unfollow（ReceivedAt=08:00:01，同秒更早接收）——unfollow 被挡
        var follow = Follow("openid-7b", same);
        follow = follow with { ReceivedAt = same.AddSeconds(2) };
        var unfollow = Unfollow("openid-7b", same);
        unfollow = unfollow with { ReceivedAt = same.AddSeconds(1) };

        await user.Use<IFollowStateService>().RecordAsync(follow, CancellationToken.None);
        var stale = await user.Use<IFollowStateService>().RecordAsync(unfollow, CancellationToken.None);

        Assert.True(stale.Recorded);
        Assert.False(stale.StateChanged);
        Assert.True(stale.State!.IsFollowing);
    }

    // ── 4. 非翻状态事件（message/menu_click——只记事实不建状态行）──

    /// <summary>消息事件：事件行落库但状态行不建（消除「未关注却因消息建状态行」二义性）。</summary>
    [Fact]
    public async Task RecordAsync_MessageEvent_RecordsFactOnly_NoStateRow()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var occurred = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        var result = await user.Use<IFollowStateService>().RecordAsync(new ChannelEvent
        {
            ChannelId = "mp-1",
            ChannelType = "wechat_event",
            ExternalUserId = "openid-8",
            EventType = "message",
            MessageId = "msg-100",
            OccurredAt = occurred,
            ReceivedAt = occurred.AddSeconds(1),
        }, CancellationToken.None);

        Assert.True(result.Recorded);
        Assert.False(result.StateChanged);
        Assert.Null(result.State);

        var state = await user.Use<FollowStateEntityDataService>()
            .EntityGetAsync(s => s.ExternalUserId == "openid-8", CancellationToken.None);
        Assert.Null(state);

        var evt = await user.Use<FollowEventEntityDataService>()
            .EntityGetAsync(e => e.ExternalUserId == "openid-8", CancellationToken.None);
        Assert.NotNull(evt);
        Assert.Equal("msg-100", evt!.MessageId);
    }

    // ── 5. 查询（GetAsync / GetByExternalUserIdAsync）──

    /// <summary>单渠道状态查询 + 跨渠道查询（业务问「任意渠道还关注着吗」）。</summary>
    [Fact]
    public async Task Get_And_GetByExternalUserId_ReturnStates()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var t1 = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        await user.Use<IFollowStateService>().RecordAsync(Follow("openid-9", t1, channelId: "mp-1"), CancellationToken.None);
        await user.Use<IFollowStateService>().RecordAsync(Follow("openid-9", t1, channelId: "mp-2"), CancellationToken.None);

        var single = await user.Use<IFollowStateService>().GetAsync("mp-1", "openid-9", CancellationToken.None);
        Assert.NotNull(single);
        Assert.Equal("mp-1", single!.ChannelId);
        Assert.True(single.IsFollowing);

        var across = await user.Use<IFollowStateService>().GetByExternalUserIdAsync("openid-9", CancellationToken.None);
        Assert.Equal(2, across.Count);
        Assert.All(across, s => Assert.True(s.IsFollowing));

        // 未绑定用户 → null / 空列表
        Assert.Null(await user.Use<IFollowStateService>().GetAsync("mp-1", "no-such-openid", CancellationToken.None));
        Assert.Empty(await user.Use<IFollowStateService>().GetByExternalUserIdAsync("no-such-openid", CancellationToken.None));
    }

    // ── 6. 日粒度统计（GetDailyStatsAsync——派生消费方 WeChatOperations 订阅统计）──

    /// <summary>跨日分桶：每日 NewFollows/Unsubscribes/NetGrowth + Source 过滤。</summary>
    [Fact]
    public async Task GetDailyStats_BucketsByDay_WithSourceFilter()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var day1 = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);
        var day1b = new DateTime(2026, 10, 20, 15, 30, 0, DateTimeKind.Unspecified);
        var day2 = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Unspecified);

        var svc = user.Use<IFollowStateService>();
        await svc.RecordAsync(ScanFollow("d1-a", day1, "qr_s1"), CancellationToken.None);
        await svc.RecordAsync(ScanFollow("d1-b", day1b, "qr_s2"), CancellationToken.None);
        await svc.RecordAsync(Unfollow("d1-c", day2), CancellationToken.None);

        var from = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Unspecified);
        var to = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Unspecified);

        // 全量
        var stats = await svc.GetDailyStatsAsync("mp-1", from, to, null, CancellationToken.None);
        Assert.Equal(2, stats.Count);
        Assert.Equal(day1.Date, stats[0].Date);
        Assert.Equal(2, stats[0].NewFollows);
        Assert.Equal(0, stats[0].Unsubscribes);
        Assert.Equal(2, stats[0].NetGrowth);
        Assert.Equal(day2.Date, stats[1].Date);
        Assert.Equal(0, stats[1].NewFollows);
        Assert.Equal(1, stats[1].Unsubscribes);
        Assert.Equal(-1, stats[1].NetGrowth);

        // Source 过滤
        var filtered = await svc.GetDailyStatsAsync("mp-1", from, to, "qr_s1", CancellationToken.None);
        Assert.Single(filtered);
        Assert.Equal(1, filtered[0].NewFollows);
        Assert.Equal("qr_s1", filtered[0].Source);
    }

    // ── 7. 重建（RebuildAsync——对账用，事件流重放修复状态）──

    /// <summary>状态行缺失/错误 → 按事件时间线重放修复（Events 排序 OccurredAt→ReceivedAt）。</summary>
    [Fact]
    public async Task Rebuild_ReplaysEventTimeline_FixesState()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var t1 = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);
        var t2 = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Unspecified);

        var svc = user.Use<IFollowStateService>();
        await svc.RecordAsync(Follow("openid-10", t1), CancellationToken.None);
        await svc.RecordAsync(Unfollow("openid-10", t2), CancellationToken.None);

        // 篡改状态行（模拟漂移：改成关注 + 错误时间）
        var state = await user.Use<FollowStateEntityDataService>()
            .EntityGetAsync(s => s.ExternalUserId == "openid-10", CancellationToken.None);
        state!.IsFollowing = true;
        state.LastEventType = "follow";
        state.LastEventAt = t1;
        await user.Use<FollowStateEntityDataService>().EntityUpdateAsync(state, CancellationToken.None);

        var corrected = await svc.RebuildAsync("mp-1", CancellationToken.None);
        Assert.Equal(1, corrected);

        var rebuilt = await user.Use<FollowStateEntityDataService>()
            .EntityGetAsync(s => s.ExternalUserId == "openid-10", CancellationToken.None);
        Assert.False(rebuilt!.IsFollowing);
        Assert.Equal("unfollow", rebuilt.LastEventType);
        Assert.Equal(t2, rebuilt.LastEventAt);
        Assert.Equal(t2, rebuilt.UnsubscribedAt);
    }

    /// <summary>重建幂等：状态已正确 → 0 行修正（不产生噪音更新）。</summary>
    [Fact]
    public async Task Rebuild_CorrectState_ReturnsZero()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var t1 = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        var svc = user.Use<IFollowStateService>();
        await svc.RecordAsync(Follow("openid-11", t1), CancellationToken.None);

        var corrected = await svc.RebuildAsync("mp-1", CancellationToken.None);
        Assert.Equal(0, corrected);
    }

    // ── 8. 清理（PurgeEventsAsync——保留期清理，消费方定时任务驱动）──

    /// <summary>清理早于保留线的事件（按 OccurredAt < beforeUtc 批量删）。</summary>
    [Fact]
    public async Task PurgeEvents_DeletesExpiredOnly()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var old = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified);
        var fresh = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        var svc = user.Use<IFollowStateService>();
        await svc.RecordAsync(Follow("openid-12", old), CancellationToken.None);
        await svc.RecordAsync(Follow("openid-13", fresh), CancellationToken.None);

        var before = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var deleted = await svc.PurgeEventsAsync(before, 500, CancellationToken.None);
        Assert.Equal(1, deleted);

        var remaining = await user.Use<FollowEventEntityDataService>()
            .EntitySelectAsync(null, ct: CancellationToken.None);
        Assert.Single(remaining);
        Assert.Equal("openid-13", remaining[0].ExternalUserId);

        // 幂等：再清一次 0 行
        Assert.Equal(0, await svc.PurgeEventsAsync(before, 500, CancellationToken.None));
    }
}
