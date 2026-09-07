using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Utility.Tags;
using TKWF.Ext.Tagging;

namespace TKWF.Ext.Tagging.Tests;

/// <summary>
/// Tagging V0.3.0 持久化测试——ITagRuleStore（规则 CRUD + 唯一约束幂等 + LoadRules 供给 ITagService 全链路）、
/// ITagHitStore（批量落库 + 分页 + 时间过滤）、ITagAnalysisService（频次/趋势/维度分布聚合）。
/// </summary>
public class TaggingV03Tests
{
    /// <summary>创建 SQLite :memory: + 建表（TagRule + TagHit + DataService 链）。</summary>
    private static (IFreeSql fsql, TagRuleEntityDataService ruleDs, TagHitRecordEntityDataService hitDs) CreateHost()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<TagRuleEntity>();
        fsql.CodeFirst.SyncStructure<TagHitRecordEntity>();

        var ruleDs = new TagRuleEntityDataService(new StubDomainUser(),
            new FreeSqlEntityDAC<TagRuleEntity>(new UnitOfWorkManager(fsql)));
        var hitDs = new TagHitRecordEntityDataService(new StubDomainUser(),
            new FreeSqlEntityDAC<TagHitRecordEntity>(new UnitOfWorkManager(fsql)));
        return (fsql, ruleDs, hitDs);
    }

    private static TagRule NewRule(string dimension, string tagName, string pattern, TagMatchMode mode = TagMatchMode.Contains)
        => new() { Dimension = dimension, TagName = tagName, Pattern = pattern, MatchMode = mode };

    // ── ITagRuleStore（FreeSqlTagRuleStore）──

    [Fact]
    public async Task RuleStore_CreateAndGetAll_Works()
    {
        var (_, ruleDs, _) = CreateHost();
        var store = new FreeSqlTagRuleStore(ruleDs, NullLogger<FreeSqlTagRuleStore>.Instance);

        var id = await store.CreateAsync(NewRule("Category", "电子", "手机"));
        Assert.NotNull(id);
        Assert.True(id > 0);

        var all = await store.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("电子", all[0].TagName);
    }

    [Fact]
    public async Task RuleStore_CreateDuplicate_ReturnsSameId_NotDuplicate()
    {
        var (_, ruleDs, _) = CreateHost();
        var store = new FreeSqlTagRuleStore(ruleDs, NullLogger<FreeSqlTagRuleStore>.Instance);

        var id1 = await store.CreateAsync(NewRule("Category", "电子", "手机"));
        var id2 = await store.CreateAsync(NewRule("Category", "电子", "手机"));   // 业务键幂等

        Assert.Equal(id1, id2);   // 同 (Dimension,TagName,Pattern) → 既有 Id
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task RuleStore_GetEnabled_FiltersDisabled()
    {
        var (_, ruleDs, _) = CreateHost();
        var store = new FreeSqlTagRuleStore(ruleDs, NullLogger<FreeSqlTagRuleStore>.Instance);

        var r1 = NewRule("Category", "电子", "手机");
        var r2 = NewRule("Category", "家电", "冰箱");
        r2.IsEnabled = false;
        await store.CreateAsync(r1);
        await store.CreateAsync(r2);

        var enabled = await store.GetEnabledAsync();
        Assert.Single(enabled);
        Assert.Equal("电子", enabled[0].TagName);
    }

    [Fact]
    public async Task RuleStore_Update_ModifiesRule()
    {
        var (_, ruleDs, _) = CreateHost();
        var store = new FreeSqlTagRuleStore(ruleDs, NullLogger<FreeSqlTagRuleStore>.Instance);

        await store.CreateAsync(NewRule("Category", "电子", "手机"));
        var updated = NewRule("Category", "电子", "手机");
        updated.Priority = 99;
        Assert.True(await store.UpdateAsync(updated));

        var all = await store.GetAllAsync();
        Assert.Equal(99, all[0].Priority);
    }

    [Fact]
    public async Task RuleStore_Delete_RemovesRule()
    {
        var (_, ruleDs, _) = CreateHost();
        var store = new FreeSqlTagRuleStore(ruleDs, NullLogger<FreeSqlTagRuleStore>.Instance);

        var id = await store.CreateAsync(NewRule("Category", "电子", "手机"));
        Assert.True(await store.DeleteAsync(id!.Value));
        Assert.Empty(await store.GetAllAsync());
    }

    // ── LoadRules 供给 ITagService 全链路（规则 → 命中）──

    [Fact]
    public async Task RuleStore_LoadRules_FeedsTagService()
    {
        var (_, ruleDs, _) = CreateHost();
        var store = new FreeSqlTagRuleStore(ruleDs, NullLogger<FreeSqlTagRuleStore>.Instance);

        await store.CreateAsync(NewRule("Category", "电子", "手机", TagMatchMode.Contains));
        var rules = await store.GetAllAsync();

        // 经 TaggingExtensionInitializer.ConfigureServices 的真实 DI 注册构建 TagService（对齐消费方路径）
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        new TaggingExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        using var sp = services.BuildServiceProvider();
        var tagService = sp.GetRequiredService<ITagService>();

        tagService.LoadRules(rules);
        var hits = tagService.GetTags("我喜欢新款手机");

        Assert.Single(hits);
        Assert.Equal("电子", hits[0].TagName);
    }

    // ── ITagHitStore（FreeSqlTagHitStore）──

    [Fact]
    public async Task HitStore_RecordAndQuery_Work()
    {
        var (_, _, hitDs) = CreateHost();
        var store = new FreeSqlTagHitStore(hitDs, NullLogger<FreeSqlTagHitStore>.Instance);
        var fixedTime = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        await store.RecordHitsAsync(new[]
        {
            new TagHit("Category", "电子", "手机", 3, 2, 1, null),
            new TagHit("Category", "家电", "冰箱", 8, 2, 1, null),
        }, sourceText: "测试原文", hitTime: fixedTime);

        var recent = await store.GetRecentAsync(10);
        Assert.Equal(2, recent.Count);
        Assert.Contains(recent, h => h.TagName == "电子");   // MatchedValue = "手机"（TagHit 模型，SourceText 快照不映射入 TagHit）

        var byDim = await store.GetByDimensionAsync("Category", null, null, 0, 10);
        Assert.Equal(2, byDim.Count);

        // P2-4：SourceText 落库验证——直查实体（DTO/TagHit 映射丢弃 SourceText，实体验证快照持久化）
        var entities = await hitDs.GetRecentAsync(10, CancellationToken.None);
        Assert.All(entities, e => Assert.Equal("测试原文", e.SourceText));
        // SQLite 时区容错：FreeSql SQLite 把 UTC DateTime 本地化存取（12:00 UTC → 20:00 Unspecified）——
        // 读出当本地时间转 UTC 断言（对齐 Notifications AssertRecent 模式）；生产 PG 存 UTC 无偏移
        Assert.All(entities, e =>
        {
            var actualUtc = e.HitTime.Kind == DateTimeKind.Utc
                ? e.HitTime
                : DateTime.SpecifyKind(e.HitTime, DateTimeKind.Local).ToUniversalTime();
            Assert.Equal(fixedTime, actualUtc);
        });
    }

    // ── ITagAnalysisService（FreeSqlTagAnalysisService）──

    [Fact]
    public async Task AnalysisService_GetFrequency_ReturnsTopN()
    {
        var (_, _, hitDs) = CreateHost();
        var hitStore = new FreeSqlTagHitStore(hitDs, NullLogger<FreeSqlTagHitStore>.Instance);
        var analysis = new FreeSqlTagAnalysisService(hitDs, NullLogger<FreeSqlTagAnalysisService>.Instance);
        var t = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        await hitStore.RecordHitsAsync(new[]
        {
            new TagHit("Category", "电子", "手机", 0, 2, 1, null),
            new TagHit("Category", "电子", "手机", 5, 2, 1, null),
            new TagHit("Category", "家电", "冰箱", 9, 2, 1, null),
        }, hitTime: t);

        var freq = await analysis.GetFrequencyAsync("Category", null, null, 10);
        Assert.Equal(2, freq.Count);
        Assert.Equal("电子", freq[0].TagName);   // 频次最高排前
        Assert.Equal(2, freq[0].Count);
    }

    [Fact]
    public async Task AnalysisService_GetDimensionDistribution_Works()
    {
        var (_, _, hitDs) = CreateHost();
        var hitStore = new FreeSqlTagHitStore(hitDs, NullLogger<FreeSqlTagHitStore>.Instance);
        var analysis = new FreeSqlTagAnalysisService(hitDs, NullLogger<FreeSqlTagAnalysisService>.Instance);
        var t = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        await hitStore.RecordHitsAsync(new[]
        {
            new TagHit("Category", "电子", "手机", 0, 2, 1, null),
            new TagHit("Brand", "苹果", "Apple", 0, 5, 1, null),
        }, hitTime: t);

        var dist = await analysis.GetDimensionDistributionAsync(null, null);
        Assert.Equal(2, dist.Count);
        Assert.Contains(dist, d => d.Dimension == "Category" && d.Count == 1);
        Assert.Contains(dist, d => d.Dimension == "Brand" && d.Count == 1);
    }

    // ── Oracle P1-1：GetTrendAsync + BucketKey 测试（CONDITION 1）──

    [Fact]
    public async Task AnalysisService_GetTrend_ReturnsBuckets()
    {
        var (_, _, hitDs) = CreateHost();
        var hitStore = new FreeSqlTagHitStore(hitDs, NullLogger<FreeSqlTagHitStore>.Instance);
        var analysis = new FreeSqlTagAnalysisService(hitDs, NullLogger<FreeSqlTagAnalysisService>.Instance);
        // 2026-09-01 是周二——跨 3 个自然日（同日不同时测 Hour 桶，跨日测 Day 桶）
        var t1 = new DateTime(2026, 9, 1, 10, 30, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 9, 1, 14, 0, 0, DateTimeKind.Utc);   // 同 Day 桶，不同 Hour 桶
        var t3 = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc);    // 跨 Day 桶

        // 逐条落库精确时间（t1/t2 同标签电子，t3 家电）
        await hitStore.RecordHitsAsync(new[] { new TagHit("Category", "电子", "手机", 0, 2, 1, null) }, hitTime: t1);
        await hitStore.RecordHitsAsync(new[] { new TagHit("Category", "电子", "手机", 5, 2, 1, null) }, hitTime: t2);
        await hitStore.RecordHitsAsync(new[] { new TagHit("Category", "家电", "冰箱", 9, 2, 1, null) }, hitTime: t3);

        var trend = await analysis.GetTrendAsync("Category", null, t1.Date, t3.Date.AddDays(1), TagGranularity.Hour);
        // Hour 粒度：t1(10:30)/t2(14:00)/t3(09:00) 各占一个 Hour 桶（电子拆成 10:00 + 14:00 两点）→ 3 桶各 Count 1
        Assert.Equal(3, trend.Count);
        Assert.All(trend, p => Assert.Equal(1, p.Count));
        Assert.Contains(trend, p => p.TagName == "电子");
        Assert.Contains(trend, p => p.TagName == "家电");

        var trendDay = await analysis.GetTrendAsync("Category", "电子", t1.Date, t3.Date.AddDays(1), TagGranularity.Day);
        // Day 粒度：电子 t1/t2 同 9/1 桶合并 → Count 2（t3 是家电被 tagName 过滤）
        Assert.Single(trendDay);
        Assert.Equal("电子", trendDay[0].TagName);
        Assert.Equal(2, trendDay[0].Count);
    }

    [Fact]
    public void BucketKey_HourDayWeekMonth_Correct()
    {
        var tuesday = new DateTime(2026, 9, 1, 10, 30, 0, DateTimeKind.Utc);   // 周二
        var sunday = new DateTime(2026, 9, 6, 20, 0, 0, DateTimeKind.Utc);     // 周日——应归 8/31(周一) 起的周

        // Hour：整点
        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            TaggingAggregations.BucketKey(tuesday, TagGranularity.Hour));
        // Day：当日零时
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            TaggingAggregations.BucketKey(tuesday, TagGranularity.Day));
        // Week：周二 → 当周周一(8/31)
        Assert.Equal(new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            TaggingAggregations.BucketKey(tuesday, TagGranularity.Week));
        // Week：周日 → 归上一周周一(8/31)——验证 (int)DayOfWeek + 6) % 7 偏移
        Assert.Equal(new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            TaggingAggregations.BucketKey(sunday, TagGranularity.Week));
        // Month：当月一日
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            TaggingAggregations.BucketKey(tuesday, TagGranularity.Month));
    }

    // ── Oracle P1-2：Hit 时间过滤分支测试（CONDITION 2）──

    [Fact]
    public async Task HitStore_GetByDimension_TimeFiltered()
    {
        var (_, _, hitDs) = CreateHost();
        var store = new FreeSqlTagHitStore(hitDs, NullLogger<FreeSqlTagHitStore>.Instance);
        var t1 = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

        await store.RecordHitsAsync(new[] { new TagHit("Category", "电子", "手机", 0, 2, 1, null) }, hitTime: t1);
        await store.RecordHitsAsync(new[] { new TagHit("Category", "家电", "冰箱", 5, 2, 1, null) }, hitTime: t2);
        await store.RecordHitsAsync(new[] { new TagHit("Category", "图书", "书", 9, 2, 1, null) }, hitTime: t3);

        // (from, to) 分支：限定 [9/1 9:00, 9/1 13:00] → 命中 t1 + t2
        var both = await store.GetByDimensionAsync("Category", t1.AddHours(-1), t2.AddHours(1), 0, 10);
        Assert.Equal(2, both.Count);
        Assert.Contains(both, h => h.TagName == "电子");
        Assert.Contains(both, h => h.TagName == "家电");

        // (from, null) 分支：>= 9/1 11:00 → 命中 t2 + t3
        var fromOnly = await store.GetByDimensionAsync("Category", t1.AddHours(1), null, 0, 10);
        Assert.Equal(2, fromOnly.Count);

        // (null, to) 分支：<= 9/1 11:00 → 命中 t1
        var toOnly = await store.GetByDimensionAsync("Category", null, t2.AddHours(-1), 0, 10);
        Assert.Single(toOnly);
        Assert.Equal("电子", toOnly[0].TagName);
    }
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约。</summary>
internal sealed class StubDomainUser : IDomainUser
{
    public string SessionKey => "test";
    public bool IsAuthenticated => false;
    public bool IsSystemActor => false;
    public IUserInfo? UserInfo => null;
    public long? TenantId => null;
    public bool IsNoAuditActive => false;
    public string? UserId => null;
    public string? UserName => null;
    public bool IsInRole(string role) => false;
    public TDomainService Use<TDomainService>() where TDomainService : IDomainService => throw new NotSupportedException();
    public TService GetService<TService>() where TService : notnull => throw new NotSupportedException();
    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}