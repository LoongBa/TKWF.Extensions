using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TKWF.Ext.Metrics;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// IMetricResultStore 契约语义测试（v0.2.0）——TestMetricResultStore + SQLite 内存库全链路
/// （真实 FreeSqlEntityDAC 驱动 DataService，红线合规测试模式）。
/// <para>覆盖：Save 落库 / Query 分页过滤 / Take 钳制（P4）/ Count / CleanupExpired
/// （过期删 + 分批循环清完 C3）/ Value 多类型边界 / 空 rows 幂等。</para>
/// </summary>
public class MetricResultStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>固定测试基准时间——SQLite UTC 存储陷阱（FreeSql 写侧将 Utc 转本地时区 +08h）：
    /// 固定刻钟用 Unspecified Kind（FreeSql 原样存储/比较），避免 00:00 边界偏移误判（对齐 AuditLogging 测试先例）。</summary>
    private static DateTime BaseTime => new(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static MetricResultRow Row(
        string name, object? value, string specKey = "sales", DateTime? atUtc = null,
        string? unit = null, string? dimensionsJson = null)
        => new(specKey, name, value, unit, dimensionsJson, atUtc ?? BaseTime);

    [Fact]
    public async Task Save_ReturnsAffectedRows_And_QueryRetrievesRows()
    {
        using var fsql = MetricResultStoreTestHost.CreateInMemoryFreeSql();
        var store = MetricResultStoreTestHost.CreateStore(fsql);

        var rows = new List<MetricResultRow>
        {
            Row("repurchase-rate-30d", 0.25m, unit: "%", dimensionsJson: """{"bucket":"2026-09"}"""),
            Row("aov-cny", 88.5m, unit: "CNY"),
        };
        var affected = await store.SaveAsync(rows, Ct);

        Assert.Equal(2, affected);

        var queried = await store.QueryAsync(new MetricResultQuery(Take: 100), Ct);
        Assert.Equal(2, queried.Count);
        Assert.Equal("repurchase-rate-30d", queried[0].Name);
        Assert.Equal("sales", queried[0].SpecKey);
        Assert.Equal("""{"bucket":"2026-09"}""", queried[0].DimensionsJson);
        Assert.Equal("aov-cny", queried[1].Name);
    }

    [Fact]
    public async Task Query_FiltersBySpecKeyNameAndTimeWindow_Inclusive()
    {
        using var fsql = MetricResultStoreTestHost.CreateInMemoryFreeSql();
        var store = MetricResultStoreTestHost.CreateStore(fsql);

        // 落库：specKey 两档（sales/dashboard）× 指标名 × 时间窗内外
        await store.SaveAsync(new List<MetricResultRow>
        {
            Row("m1", 1m, specKey: "sales", atUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified)),
            Row("m2", 2m, specKey: "sales", atUtc: new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Unspecified)),
            Row("m1", 3m, specKey: "sales", atUtc: new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Unspecified)), // 窗外
            Row("m1", 4m, specKey: "dashboard", atUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified)),
        }, Ct);

        // SpecKey 精确
        var bySpec = await store.QueryAsync(new MetricResultQuery(SpecKey: "dashboard"), Ct);
        var only = Assert.Single(bySpec);
        Assert.Equal("4", only.Value);

        // Name 精确
        var byName = await store.QueryAsync(new MetricResultQuery(Name: "m1", SpecKey: "sales", Take: 100), Ct);
        Assert.Equal(2, byName.Count);

        // 时间窗闭区间（9/1~9/2 全量含边界，8/15 排除）
        var byWindow = await store.QueryAsync(new MetricResultQuery(
            SpecKey: "sales",
            FromUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified),
            ToUtc: new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Unspecified),
            Take: 100), Ct);
        Assert.Equal(2, byWindow.Count);
        // CalculatedAtUtc 降序：9/2 在前
        Assert.Equal(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Unspecified), byWindow[0].CalculatedAtUtc);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified), byWindow[1].CalculatedAtUtc);
    }

    [Fact]
    public async Task Query_TakeClamped_ToMax200()
    {
        using var fsql = MetricResultStoreTestHost.CreateInMemoryFreeSql();
        var store = MetricResultStoreTestHost.CreateStore(fsql);

        // 落库 250 条
        var rows = Enumerable.Range(0, 250)
            .Select(i => Row($"metric-{i}", i, atUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMinutes(i)))
            .ToList();
        await store.SaveAsync(rows, Ct);

        // Take=500 → Store 静默钳制到 200（P4）
        var queried = await store.QueryAsync(new MetricResultQuery(Take: 500), Ct);
        Assert.Equal(200, queried.Count);

        // Take=0（<1）→ 按默认 50
        var zeroTake = await store.QueryAsync(new MetricResultQuery(Take: 0), Ct);
        Assert.Equal(50, zeroTake.Count);

        // Take=-1（<0）→ 0 行（P4：Take<0 视为 0 返回空集——InternalSelectAsync Take(0) 空集）
        var negativeTake = await store.QueryAsync(new MetricResultQuery(Take: -1), Ct);
        Assert.Empty(negativeTake);
    }

    [Fact]
    public async Task Query_DimensionFilter_NotImplemented_ReturnsUnfiltered()
    {
        // P1 兼容降级：测试 Store 不实现 DimensionFilter——传入维度过滤条件仍返回未按维度过滤的结果
        using var fsql = MetricResultStoreTestHost.CreateInMemoryFreeSql();
        var store = MetricResultStoreTestHost.CreateStore(fsql);

        await store.SaveAsync(new List<MetricResultRow>
        {
            Row("m1", 1m, dimensionsJson: """{"bucket":"2026-08"}"""),
            Row("m2", 2m, dimensionsJson: """{"bucket":"2026-09"}"""),
        }, Ct);

        var queried = await store.QueryAsync(new MetricResultQuery(DimensionFilter: """{"bucket":"2026-08"}"""), Ct);
        Assert.Equal(2, queried.Count);   // 降级：维度过滤被忽略，返回全量
    }

    [Fact]
    public async Task Count_MatchesSameFilters()
    {
        using var fsql = MetricResultStoreTestHost.CreateInMemoryFreeSql();
        var store = MetricResultStoreTestHost.CreateStore(fsql);

        await store.SaveAsync(new List<MetricResultRow>
        {
            Row("m1", 1m, specKey: "sales", atUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified)),
            Row("m2", 2m, specKey: "sales", atUtc: new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Unspecified)),
            Row("m1", 3m, specKey: "dashboard", atUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified)),
        }, Ct);

        var all = await store.CountAsync(new MetricResultQuery(), Ct);
        Assert.Equal(3, all);

        var byName = await store.CountAsync(new MetricResultQuery(Name: "m1"), Ct);
        Assert.Equal(2, byName);

        var bySpecAndWindow = await store.CountAsync(new MetricResultQuery(
            SpecKey: "sales",
            FromUtc: new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Unspecified)), Ct);
        Assert.Equal(1, bySpecAndWindow);

        var none = await store.CountAsync(new MetricResultQuery(Name: "no-such-metric"), Ct);
        Assert.Equal(0, none);
    }

    [Fact]
    public async Task CleanupExpired_DeletesExpired_KeepsCurrent()
    {
        using var fsql = MetricResultStoreTestHost.CreateInMemoryFreeSql();
        var store = MetricResultStoreTestHost.CreateStore(fsql);
        var now = DateTime.UtcNow;

        // 过期（40 天前）×2 + 保留期内（10 天前）×1
        await store.SaveAsync(new List<MetricResultRow>
        {
            Row("expired-1", 1m, atUtc: now.AddDays(-40)),
            Row("expired-2", 2m, atUtc: now.AddDays(-40)),
            Row("current", 3m, atUtc: now.AddDays(-10)),
        }, Ct);

        var deleted = await store.CleanupExpiredAsync(retentionDays: 30, batchSize: 10, ct: Ct);

        Assert.Equal(2, deleted);

        var remaining = await store.QueryAsync(new MetricResultQuery(Take: 100), Ct);
        var only = Assert.Single(remaining);
        Assert.Equal("current", only.Name);
    }

    [Fact]
    public async Task CleanupExpired_BatchLoop_DeletesAllExpiredInOneCall()
    {
        using var fsql = MetricResultStoreTestHost.CreateInMemoryFreeSql();
        var store = MetricResultStoreTestHost.CreateStore(fsql);
        var now = DateTime.UtcNow;

        // 过期 250 条 > batchSize(50)——单次调用须循环分批清完（C3）
        var expired = Enumerable.Range(0, 250)
            .Select(i => Row($"expired-{i}", i, atUtc: now.AddDays(-40).AddMinutes(-i)))
            .ToList();
        await store.SaveAsync(expired, Ct);
        // 保留期内 10 条——不应被删
        await store.SaveAsync(Enumerable.Range(0, 10)
            .Select(i => Row($"current-{i}", i, atUtc: now.AddDays(-1))).ToList(), Ct);

        var deleted = await store.CleanupExpiredAsync(retentionDays: 30, batchSize: 50, ct: Ct);

        Assert.Equal(250, deleted);

        var remaining = await store.QueryAsync(new MetricResultQuery(Take: 100), Ct);
        Assert.Equal(10, remaining.Count);
        Assert.All(remaining, r => Assert.StartsWith("current-", r.Name));
    }

    [Fact]
    public async Task Value_MultiTypeBoundary_DecimalStringNull_RoundTrip()
    {
        using var fsql = MetricResultStoreTestHost.CreateInMemoryFreeSql();
        var store = MetricResultStoreTestHost.CreateStore(fsql);

        var rows = new List<MetricResultRow>
        {
            Row("decimal-val", 12.5m, unit: "CNY"),          // decimal → JSON 化 ValueText "12.5"
            Row("string-val", "direct-text"),                 // string 直通
            Row("null-val", null),                            // null → null
        };
        await store.SaveAsync(rows, Ct);

        var queried = await store.QueryAsync(new MetricResultQuery(Take: 100), Ct);

        Assert.Equal(3, queried.Count);
        // 落库往返：decimal → "12.5"（JsonSerializer 序列化，复用 MetricResultMapper.JsonOptions）
        Assert.Equal("12.5", queried.Single(r => r.Name == "decimal-val").Value);
        // string 直通原样
        Assert.Equal("direct-text", queried.Single(r => r.Name == "string-val").Value);
        // null → null
        var nullRow = queried.Single(r => r.Name == "null-val");
        Assert.Null(nullRow.Value);

        // 计数仍全量（null 值行不丢失）
        Assert.Equal(3, await store.CountAsync(new MetricResultQuery(), Ct));
    }

    [Fact]
    public async Task Save_EmptyRows_IsIdempotent_ReturnsZero_NoThrow()
    {
        using var fsql = MetricResultStoreTestHost.CreateInMemoryFreeSql();
        var store = MetricResultStoreTestHost.CreateStore(fsql);

        var affected = await store.SaveAsync([], Ct);

        Assert.Equal(0, affected);
        Assert.Equal(0, await store.CountAsync(new MetricResultQuery(), Ct));
    }
}