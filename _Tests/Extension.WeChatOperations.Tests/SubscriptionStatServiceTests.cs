using System;
using System.Linq;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="ISubscriptionStatService"/>（SubscriptionStatService 门面）测试——I2-10 派生改造：
/// 写路径（RecordAsync）已删除归编排层 <see cref="IFollowStateService"/>，本门面只读委托
/// <see cref="IFollowStateService.GetDailyStatsAsync"/> 日统计映射 <see cref="SubscriptionStatPointDto"/>。
/// <para>生产路径：<c>user.Use&lt;ISubscriptionStatService&gt;()</c> AOP（AddConstructibleService 守卫工厂）；
/// 种子数据经 <c>FollowEventEntityDataService.EntityCreateAsync</c> 生产路径直写（事件日志 = 统计口径真相源——
/// 关注域统计与事件日志一致验收）。</para>
/// </summary>
public class SubscriptionStatServiceTests : WeChatOperationsTestHost
{
    /// <summary>
    /// GetTrendAsync 委托关注域日统计 → 映射 SubscriptionStatPointDto（Date/NewFollows/Unsubscribes/NetGrowth——
    /// 与 FollowDailyStatDto 同形一致）；两日多点升序。
    /// </summary>
    [Fact]
    public async Task GetTrendAsync_MapsDailyStats_NewFollowsUnsubscribesNetGrowth()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 2, unfollow: 1);
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 11), follow: 1, unfollow: 0);

        var trend = await service.GetTrendAsync("ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 11),
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, trend.Count);
        Assert.Equal(Utc(2026, 10, 10), trend[0].Date);
        Assert.Equal(2, trend[0].NewFollows);
        Assert.Equal(1, trend[0].Unsubscribes);
        Assert.Equal(1, trend[0].NetGrowth);        // 2 - 1
        Assert.Null(trend[0].Source);
        Assert.Equal(Utc(2026, 10, 11), trend[1].Date);
        Assert.Equal(1, trend[1].NewFollows);
        Assert.Equal(0, trend[1].Unsubscribes);
        Assert.Equal(1, trend[1].NetGrowth);
    }

    /// <summary>
    /// GetTrendAsync source 过滤透传——<c>source=null</c> 全来源合计（含来源行与空来源行）；
    /// 非空 = 仅该来源维度（对齐关注域 Source 语义——I2-10 派生后空值语义由"仅汇总行"变更为"全来源合计"）。
    /// </summary>
    [Fact]
    public async Task GetTrendAsync_SourceFilter_PassThrough()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 1, unfollow: 0, source: WeChatSubscriptionSources.Search);
        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 1, unfollow: 0, source: null);

        // search 来源查询（非空——仅该来源行）
        var search = await service.GetTrendAsync("ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 10),
            source: WeChatSubscriptionSources.Search, ct: TestContext.Current.CancellationToken);
        Assert.Single(search);
        Assert.Equal(WeChatSubscriptionSources.Search, search[0].Source);
        Assert.Equal(1, search[0].NewFollows);

        // 全来源合计（source=null——含 search 与空来源两行）
        var all = await service.GetTrendAsync("ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 10),
            source: null, ct: TestContext.Current.CancellationToken);
        Assert.Single(all);
        Assert.Null(all[0].Source);
        Assert.Equal(2, all[0].NewFollows);

        // 未记录来源（share）→ 空
        var share = await service.GetTrendAsync("ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 10),
            source: WeChatSubscriptionSources.Share, ct: TestContext.Current.CancellationToken);
        Assert.Empty(share);
    }

    /// <summary>区间外事件不返回（半开区间语义——to 日 +1 天界）：10-10 事件在 10-11~10-12 区间为空。</summary>
    [Fact]
    public async Task GetTrendAsync_DateRange_ExcludesOutside()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await SeedFollowEventsAsync(user, "ch-stat", Utc(2026, 10, 10), follow: 1, unfollow: 0);

        var empty = await service.GetTrendAsync("ch-stat", Utc(2026, 10, 11), Utc(2026, 10, 12),
            ct: TestContext.Current.CancellationToken);
        Assert.Empty(empty);
    }

    /// <summary>无事件 → 空列表（无行不造假零点）。</summary>
    [Fact]
    public async Task GetTrendAsync_NoEvents_Empty()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        var trend = await service.GetTrendAsync("ch-stat", Utc(2026, 10, 10), Utc(2026, 10, 12),
            ct: TestContext.Current.CancellationToken);
        Assert.Empty(trend);
    }

    /// <summary>UTC 午夜种子日期（对齐测试宿主 DateTimeKind=Utc 存储语义）。</summary>
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
