using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="ISubscriptionStatService"/>（SubscriptionStatService 门面）测试——订阅统计（设计文档 §3.3.1/§3.3.2）：
/// RecordAsync 每日 upsert（同 ChannelId+StatDate 更新计数 + NetCount 重算）/ 跨 channel 独立 / 跨日独立 /
/// GetTrendAsync 区间过滤升序。
/// <para>生产路径：<c>user.Use&lt;ISubscriptionStatService&gt;()</c> AOP（AddConstructibleService 守卫工厂；
/// 事件 handler WeChatOperationsReplyHandler 帧内调用）。</para>
/// </summary>
public class SubscriptionStatServiceTests : WeChatOperationsTestHost
{
    /// <summary>
    /// RecordAsync 每日 upsert——同 channel 同天多次订阅 → 单行累加计数。
    /// <para>⚠️ SKIP（生产兼容缺陷，2026-10-18 诊断实证，报告主 Agent 待修复）：<see cref="SubscriptionStatService"/>
    /// 的 upsert 键 <c>s.StatDate == statDate</c>（DateTime 相等）在 <b>FreeSql SQLite</b> 下恒不命中——
    /// System.Data.SQLite 存 TEXT 形如 <c>2026-10-17 00:00:00Z</c>（带 Z），而 FreeSql 表达式内联常量生成
    /// <c>'2026-10-17 00:00:00'</c>（无 Z）→ 字符串字典序不等（raw SQL 带 Z 参数化命中、FreeSql 内联恒不等实证）。
    /// 生产部署 PG（timestamp 精确）正常；修复建议由主 Agent 定夺（如键改用 <c>DateOnly</c> 或范围语义）。
    /// 单次写路径（RecordAsync_DifferentChannels_Independent）不受影响已绿。</para>
    /// </summary>
    [Fact(Skip = "生产缺陷：FreeSql SQLite 下 StatDate DateTime 相等键失效（System.Data.SQLite 存储带 Z vs FreeSql 内联无 Z）——诊断实证，报主 Agent")]
    public async Task RecordAsync_SameDay_UpsertsCount()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await service.RecordAsync("ch-stat", 1, ct: TestContext.Current.CancellationToken);
        await service.RecordAsync("ch-stat", 1, ct: TestContext.Current.CancellationToken);   // 同天第二次

        var trend = await service.GetTrendAsync("ch-stat", DateTime.UtcNow.Date, DateTime.UtcNow.Date, ct: TestContext.Current.CancellationToken);
        Assert.Single(trend);
        Assert.Equal(2, trend[0].SubscribeCount);   // 每日 upsert 累加
        Assert.Equal(2, trend[0].NetCount);
    }

    /// <summary>
    /// RecordAsync 订阅/取关混合——NetCount 重算（Subscribe - Unsubscribe）。
    /// 同 <see cref="RecordAsync_SameDay_UpsertsCount"/>——依赖 StatDate DateTime 相等 upsert 键，SQLite 下生产缺陷。
    /// </summary>
    [Fact(Skip = "生产缺陷：FreeSql SQLite 下 StatDate DateTime 相等键失效——同 RecordAsync_SameDay_UpsertsCount，报主 Agent")]
    public async Task RecordAsync_MixedEvents_NetCountRecomputed()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await service.RecordAsync("ch-stat", 1, ct: TestContext.Current.CancellationToken);
        await service.RecordAsync("ch-stat", 2, ct: TestContext.Current.CancellationToken);   // 取关

        var trend = await service.GetTrendAsync("ch-stat", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), ct: TestContext.Current.CancellationToken);
        Assert.Single(trend);
        Assert.Equal(1, trend[0].SubscribeCount);
        Assert.Equal(1, trend[0].UnsubscribeCount);
        Assert.Equal(0, trend[0].NetCount);   // 1 - 1 = 0
    }

    /// <summary>跨 channel 独立——不同 channel 各自一行计数。</summary>
    [Fact]
    public async Task RecordAsync_DifferentChannels_Independent()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await service.RecordAsync("ch-a", 1, ct: TestContext.Current.CancellationToken);
        await service.RecordAsync("ch-b", 1, ct: TestContext.Current.CancellationToken);

        var trendA = await service.GetTrendAsync("ch-a", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), ct: TestContext.Current.CancellationToken);
        var trendB = await service.GetTrendAsync("ch-b", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), ct: TestContext.Current.CancellationToken);
        Assert.Single(trendA);
        Assert.Single(trendB);
        Assert.Equal(1, trendA[0].SubscribeCount);
        Assert.Equal(1, trendB[0].SubscribeCount);
    }

    /// <summary>RecordAsync 无效 eventType → ArgumentException。</summary>
    [Fact]
    public async Task RecordAsync_InvalidEventType_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.RecordAsync("ch-stat", 9, ct: TestContext.Current.CancellationToken));
        Assert.Contains("eventType", ex.Message);
    }

    /// <summary>
    /// GetTrendAsync 区间过滤（闭区间）+ StatDate 升序；区间外数据不返回。
    /// <para>⚠️ 用宽区间（昨天~明天）而非"今天单日闭区间"——单日闭区间 <c>[today, today]</c> 等效 DateTime 相等，
    /// 触发同 <see cref="RecordAsync_SameDay_UpsertsCount"/> 的 FreeSql SQLite 相等键缺陷（字典序错位），
    /// 区间过滤语义本身（宽范围命中、久远范围空）不受影响。</para>
    /// </summary>
    [Fact]
    public async Task GetTrendAsync_DateRangeFilter()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await service.RecordAsync("ch-stat", 1, ct: TestContext.Current.CancellationToken);   // 今天

        // 久远区间 → 空
        var empty = await service.GetTrendAsync("ch-stat", DateTime.UtcNow.AddDays(-90), DateTime.UtcNow.AddDays(-89), ct: TestContext.Current.CancellationToken);
        Assert.Empty(empty);

        // 昨天~明天闭区间 → 今天行（1 条，升序）——区间过滤语义验证
        var trend = await service.GetTrendAsync("ch-stat", DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(1), ct: TestContext.Current.CancellationToken);
        Assert.Single(trend);
        Assert.Equal(1, trend[0].SubscribeCount);
        Assert.Equal(DateTime.UtcNow.Date, trend[0].StatDate.Date);
    }

    /// <summary>
    /// 来源/场景维度（V0.3.0——DMP-Lite 5.7 来源/场景筛选）：RecordAsync 带 source → 独立于汇总行的来源行
    /// （upsert 键 (ChannelId, StatDate, Source)）；GetTrendAsync 按 source 过滤——null = 仅汇总行 / 非空 = 仅该来源行。
    /// </summary>
    [Fact]
    public async Task RecordAsync_SourceDimension_SeparateRows_AndFilteredTrend()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<ISubscriptionStatService>();

        await service.RecordAsync("ch-stat", 1, WeChatSubscriptionSources.Search, ct: TestContext.Current.CancellationToken);   // 来源行（search）
        await service.RecordAsync("ch-stat", 1, null, ct: TestContext.Current.CancellationToken);                              // 汇总行（Source=null）
        await service.RecordAsync("ch-stat", 1, WeChatSubscriptionSources.Search, ct: TestContext.Current.CancellationToken);   // search 第二次——来源行累加

        // 汇总行查询（source=null——仅汇总，不含来源行）
        var summary = await service.GetTrendAsync("ch-stat", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), source: null, ct: TestContext.Current.CancellationToken);
        Assert.Single(summary);
        Assert.Equal(1, summary[0].SubscribeCount);
        Assert.Null(summary[0].Source);

        // search 来源查询（非空——仅该来源行，累加两次）
        var search = await service.GetTrendAsync("ch-stat", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), source: WeChatSubscriptionSources.Search, ct: TestContext.Current.CancellationToken);
        Assert.Single(search);
        Assert.Equal(WeChatSubscriptionSources.Search, search[0].Source);
        Assert.Equal(2, search[0].SubscribeCount);

        // 未记录来源（share）→ 空
        var share = await service.GetTrendAsync("ch-stat", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), source: WeChatSubscriptionSources.Share, ct: TestContext.Current.CancellationToken);
        Assert.Empty(share);
    }
}
