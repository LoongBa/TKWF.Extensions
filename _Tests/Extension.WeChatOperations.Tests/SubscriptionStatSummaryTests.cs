using System;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="ISubscriptionStatService.GetSummaryAsync"/>（区间汇总统计——DMP-Lite 5.7 §2"统计数据"四指标卡）测试：
/// 区间新增关注/区间取消关注/净增关注（R7 负值按 0 计）/累计增长率（以区间起始日累计净增为基数估算）。
/// <para>I2-10 派生改造：实现委托 <see cref="IFollowStateService.GetDailyStatsAsync"/> 单次区间聚合——
/// 期初基线（起始日前全部日点）与区间窗口内存切分，口径不变；种子数据经
/// <c>FollowEventEntityDataService.EntityCreateAsync</c> 生产路径直写（事件日志 = 统计口径真相源）。</para>
/// </summary>
public class SubscriptionStatSummaryTests : WeChatOperationsTestHost
{
    /// <summary>
    /// 多日区间聚合——Total/DailyAvg/Net 正确：三天（10-10~10-12）合计
    /// 12 新增 / 3 取关 / 净增 9；日均 4.0 / 1.0；无期初基线 → 增长率不可判定。
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_MultiDay_AggregatesTotalsAndDailyAverage()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 5, unfollow: 1);
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 11), follow: 3, unfollow: 2);
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 12), follow: 4, unfollow: 0);

        var summary = await service.GetSummaryAsync(
            "ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 12),
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(12, summary.TotalSubscribe);
        Assert.Equal(3, summary.TotalUnsubscribe);
        Assert.Equal(9, summary.NetCount);        // 12 - 3 = 9（非负——原样）
        Assert.Equal(9, summary.NetCountRaw);
        Assert.False(summary.IsNetNegative);
        Assert.Equal(3, summary.DayCount);        // 闭区间含首尾
        Assert.Equal(4.0, summary.DailyAverageSubscribe, 4);   // 12 ÷ 3
        Assert.Equal(1.0, summary.DailyAverageUnsubscribe, 4); // 3 ÷ 3
        Assert.Null(summary.GrowthRate);          // 无期初基线
        Assert.True(summary.GrowthRateUndeterminable);
    }

    /// <summary>
    /// 净增为负 → <see cref="SubscriptionStatSummaryDto.NetCount"/> 钳制为 0 + <see cref="SubscriptionStatSummaryDto.IsNetNegative"/> 标记
    /// （R7 口径——真实负值经 NetCountRaw 暴露）；增长率用真实净增（负增长有语义）：
    /// 期初累计 10（10-01 净 +10），区间净 -4 → 增长率 -0.4。
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_NegativeNet_ClampedZero_AndFlag()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 1), follow: 10, unfollow: 0);   // 期初基线 净 +10
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 1, unfollow: 5);   // 区间净 -4

        var summary = await service.GetSummaryAsync(
            "ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 10),
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(-4, summary.NetCountRaw);
        Assert.Equal(0, summary.NetCount);        // R7：负值按 0 计
        Assert.True(summary.IsNetNegative);
        Assert.Equal(-0.4, summary.GrowthRate!.Value, 4);   // -4 ÷ 10（真实净增——负增长有语义）
        Assert.False(summary.GrowthRateUndeterminable);
    }

    /// <summary>
    /// 累计增长率 = 区间净增 ÷ 期初累计净增（期初 = <c>Date &lt; from.Date</c> 全量日点，含起始日前一天；
    /// 边界验证——from 当日行不并入期初）：期初 10-01 净 +10 + 10-09 净 +2 = 12，区间 10-10 净 +5 → 5 ÷ 12 = 0.4167。
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_GrowthRate_PositivePriorBaseline()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 1), follow: 10, unfollow: 0);   // 期初 净 +10
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 9), follow: 5, unfollow: 3);    // 起始日前一天 净 +2（边界——并入期初）
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 6, unfollow: 1);   // 区间 净 +5

        var summary = await service.GetSummaryAsync(
            "ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 10),
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(5, summary.NetCountRaw);     // 区间净增 = 6 - 1
        Assert.Equal(0.4167, summary.GrowthRate!.Value, 4);   // 5 ÷ 12 = 0.41666… → 四舍五入 4 位
        Assert.False(summary.GrowthRateUndeterminable);
    }

    /// <summary>
    /// 期初累计 ≤ 0 → 增长率不可判定：无期初行 / 期初累计恰好为 0（净增相抵）两种情形均
    /// <see cref="SubscriptionStatSummaryDto.GrowthRate"/> = null + <see cref="SubscriptionStatSummaryDto.GrowthRateUndeterminable"/> = true（UI 显示 "—"）。
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_EmptyOrZeroPriorBaseline_GrowthRateNull()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        // ① 无期初基线
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 3, unfollow: 0);
        var noPrior = await service.GetSummaryAsync(
            "ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 10),
            ct: TestContext.Current.CancellationToken);
        Assert.Null(noPrior.GrowthRate);
        Assert.True(noPrior.GrowthRateUndeterminable);

        // ② 期初累计恰好为 0（净增相抵——正负基数均无意义）
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 1), follow: 2, unfollow: 2);    // 期初 净 0
        var zeroPrior = await service.GetSummaryAsync(
            "ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 10),
            ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, zeroPrior.NetCountRaw);   // 区间行（10-10 净 +3）正常计入
        Assert.Null(zeroPrior.GrowthRate);        // 期初累计 = 0 → 除零不可判定
        Assert.True(zeroPrior.GrowthRateUndeterminable);
    }

    /// <summary>
    /// 来源过滤（I2-10 派生后语义）——<c>source=null</c> 全来源合计（含来源行与空来源行）；
    /// 指定来源仅该来源行；跨 channel 不泄漏。
    /// </summary>
    [Fact]
    public async Task GetSummaryAsync_SourceFilter_AllSourcesVsSpecificSource()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        // 空来源行
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 5, unfollow: 1);
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 11), follow: 3, unfollow: 1);
        // search 来源行
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 2, unfollow: 0, source: WeChatSubscriptionSources.Search);
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 11), follow: 1, unfollow: 1, source: WeChatSubscriptionSources.Search);
        // 其他 channel（跨 channel 隔离）
        await SeedFollowEventsAsync(user, "ch-other", Utc(2026, 10, 10), follow: 100, unfollow: 0);

        var from = Utc(2026, 10, 10);
        var to = Utc(2026, 10, 11);

        // 全来源合计（source=null——含空来源与 search 来源两维）
        var summary = await service.GetSummaryAsync("ch-stat", from, to, source: null, ct: TestContext.Current.CancellationToken);
        Assert.Equal(11, summary.TotalSubscribe);   // 5 + 3 + 2 + 1（全来源）
        Assert.Equal(3, summary.TotalUnsubscribe);  // 1 + 1 + 0 + 1
        Assert.Equal(8, summary.NetCount);

        // search 来源查询（非空——仅该来源行）
        var search = await service.GetSummaryAsync("ch-stat", from, to, source: WeChatSubscriptionSources.Search, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, search.TotalSubscribe);    // 2 + 1（仅 search 行）
        Assert.Equal(1, search.TotalUnsubscribe);
        Assert.Equal(2, search.NetCount);

        // 未记录来源（share）→ 空区间零值
        var share = await service.GetSummaryAsync("ch-stat", from, to, source: WeChatSubscriptionSources.Share, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, share.TotalSubscribe);
        Assert.Equal(0, share.TotalUnsubscribe);
        Assert.Equal(0, share.NetCount);

        // 跨 channel 隔离——ch-other 数据不并入 ch-stat 汇总
        var other = await service.GetSummaryAsync("ch-other", from, to, ct: TestContext.Current.CancellationToken);
        Assert.Equal(100, other.TotalSubscribe);
    }

    /// <summary>UTC 午夜种子日期（对齐测试宿主 DateTimeKind=Utc 存储语义——范围查询参数化同形不触发 SQLite 字典序错位）。</summary>
    private static DateTime Utc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>种子关注事件（FollowEventEntityDataService 生产路径直写——事件日志 = 统计口径真相源；
    /// DedupeKey 逐行唯一 + OccurredAt 分钟错开防同日并列歧义）。</summary>
    private static async Task SeedFollowEventsAsync(DomainUser<TestUserInfo> user, string channelId, DateTime date,
        int follow, int unfollow, string? source = null)
    {
        var ds = user.Use<TKWF.Ext.Federation.FollowEventEntityDataService>();
        var seq = 0;
        for (var i = 0; i < follow; i++)
            await ds.EntityCreateAsync(NewFollowEvent(channelId, date, seq++, "follow", source), TestContext.Current.CancellationToken);
        for (var i = 0; i < unfollow; i++)
            await ds.EntityCreateAsync(NewFollowEvent(channelId, date, seq++, "unfollow", source), TestContext.Current.CancellationToken);
    }

    private static TKWF.Ext.Federation.FollowEventEntity NewFollowEvent(string channelId, DateTime date, int seq, string eventType, string? source)
        => new()
        {
            ChannelId = channelId,
            ExternalUserId = "seed-openid",
            EventType = eventType,
            Source = source,
            OccurredAt = date.Date.AddMinutes(seq),
            ReceivedAt = date.Date.AddMinutes(seq),
            DedupeKey = $"seed-{channelId}-{date:yyyyMMdd}-{seq}-{eventType}-{source ?? "-"}",
            CreatedAt = DateTime.UtcNow,
        };
}
