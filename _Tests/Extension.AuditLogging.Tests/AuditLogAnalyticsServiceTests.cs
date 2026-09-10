using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// AuditLogAnalyticsService（V0.3.0）测试——调用次数 TopN 聚合（ByService/ByUser + 时间窗口 + 空白维度跳过）+
/// SQL 级统计（成功/失败/平均/最大耗时 + 时间窗口）+ 保留天数清理（RetentionDays/分批循环/无过期零删除）+
/// 异常静默（DataService 异常 → 空结果/Warning 不抛）。
/// <para>沿用 AuditLoggingTestHost SQLite 内存 + 真实 FreeSqlEntityDAC 模式；时间断言用 Id 集合/计数相对容错
/// （SQLite UTC 存储陷阱——勿精确比较 DateTime）。</para>
/// </summary>
public class AuditLogAnalyticsServiceTests
{
    /// <summary>创建使用 SQLite 内存库 + 实体同步的 IFreeSql 实例（每次调用新连接 = 独立内存库）。</summary>
    private static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        return fsql;
    }

    /// <summary>经 DataService 显式 ExecutionTime 落库（清理/窗口测试需精确控制时间；原始 UTC 值——对齐既有测试先例）。</summary>
    private static async Task<AuditLogEntity> InsertAtAsync(
        AuditLogEntityDataService ds, string userName, string service, DateTime executionTimeUtc,
        bool success = true, int durationMs = 10)
        => await ds.EntityCreateAsync(new AuditLogEntity
        {
            UserName = userName,
            ServiceName = service,
            MethodName = "TestMethod",
            ExecutionTime = executionTimeUtc,
            DurationMs = durationMs,
            Success = success,
            CreateTime = DateTimeOffset.Now,
        }, CancellationToken.None);

    // ── ① 调用次数 TopN 聚合 ──

    [Fact]
    public async Task GetTopServicesAsync_ReturnsTopN()
    {
        using var fsql = CreateInMemoryFreeSql();
        var ds = AuditLoggingTestHost.CreateDataService(fsql);
        // OrderService ×3、PaymentService ×2、InventoryService ×1
        await InsertAtAsync(ds, "alice", "OrderService", DateTime.UtcNow.AddMinutes(-10));
        await InsertAtAsync(ds, "bob", "OrderService", DateTime.UtcNow.AddMinutes(-10));
        await InsertAtAsync(ds, "carol", "OrderService", DateTime.UtcNow.AddMinutes(-10));
        await InsertAtAsync(ds, "alice", "PaymentService", DateTime.UtcNow.AddMinutes(-10));
        await InsertAtAsync(ds, "bob", "PaymentService", DateTime.UtcNow.AddMinutes(-10));
        await InsertAtAsync(ds, "alice", "InventoryService", DateTime.UtcNow.AddMinutes(-10));

        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql);

        var top2 = await analytics.GetTopServicesAsync(topN: 2);
        Assert.Equal(2, top2.Count);
        Assert.Equal("OrderService", top2[0].Dimension);
        Assert.Equal(3, top2[0].Count);
        Assert.Equal("PaymentService", top2[1].Dimension);
        Assert.Equal(2, top2[1].Count);

        var top5 = await analytics.GetTopServicesAsync(topN: 5);
        Assert.Equal(3, top5.Count);      // 仅 3 个服务有记录
        Assert.Equal("InventoryService", top5[2].Dimension);
        Assert.Equal(1, top5[2].Count);
    }

    [Fact]
    public async Task GetTopServicesAsync_RespectsWindow()
    {
        using var fsql = CreateInMemoryFreeSql();
        var ds = AuditLoggingTestHost.CreateDataService(fsql);
        var now = DateTime.UtcNow;
        await InsertAtAsync(ds, "alice", "OrderService", now.AddMinutes(-10));   // 窗内 ×3
        await InsertAtAsync(ds, "alice", "OrderService", now.AddMinutes(-10));
        await InsertAtAsync(ds, "alice", "OrderService", now.AddMinutes(-10));
        await InsertAtAsync(ds, "bob", "PaymentService", now.AddHours(-2));       // 窗外 ×2
        await InsertAtAsync(ds, "bob", "PaymentService", now.AddHours(-2));

        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql);

        // 30 分钟窗口 → 仅 OrderService 计入
        var windowed = await analytics.GetTopServicesAsync(topN: 10, window: TimeSpan.FromMinutes(30));
        var order = Assert.Single(windowed);
        Assert.Equal("OrderService", order.Dimension);
        Assert.Equal(3, order.Count);

        // 全量（window: null）→ 两个服务都计入
        var all = await analytics.GetTopServicesAsync(topN: 10);
        Assert.Equal(2, all.Count);
        Assert.Equal(3, all.Single(s => s.Dimension == "OrderService").Count);
        Assert.Equal(2, all.Single(s => s.Dimension == "PaymentService").Count);
    }

    [Fact]
    public async Task GetTopUsersAsync_ExcludesNullUserName()
    {
        using var fsql = CreateInMemoryFreeSql();
        var ds = AuditLoggingTestHost.CreateDataService(fsql);
        await InsertAtAsync(ds, "alice", "OrderService", DateTime.UtcNow.AddMinutes(-10));  // alice ×2
        await InsertAtAsync(ds, "alice", "PaymentService", DateTime.UtcNow.AddMinutes(-10));
        await InsertAtAsync(ds, "bob", "InventoryService", DateTime.UtcNow.AddMinutes(-10)); // bob ×1
        await InsertAtAsync(ds, null!, "AnonymousService", DateTime.UtcNow.AddMinutes(-10)); // 匿名 ×2 不应计入
        await InsertAtAsync(ds, null!, "AnonymousService", DateTime.UtcNow.AddMinutes(-10));

        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql);
        var result = await analytics.GetTopUsersAsync(topN: 10);

        Assert.Equal(2, result.Count);
        Assert.Equal("alice", result[0].Dimension);
        Assert.Equal(2, result[0].Count);
        Assert.Equal("bob", result[1].Dimension);
        Assert.Equal(1, result[1].Count);
        Assert.All(result, s => Assert.False(string.IsNullOrWhiteSpace(s.Dimension)));
        Assert.Equal(3, result.Sum(s => s.Count));   // 匿名 2 条被跳过
    }

    [Fact]
    public async Task GetTopServicesAsync_NoData_ReturnsEmpty()
    {
        using var fsql = CreateInMemoryFreeSql();
        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql);

        Assert.Empty(await analytics.GetTopServicesAsync());
        Assert.Empty(await analytics.GetTopUsersAsync());
    }

    // ── ② SQL 级统计 ──

    [Fact]
    public async Task GetStatsAsync_CountsSucceededFailedAvgMax()
    {
        using var fsql = CreateInMemoryFreeSql();
        var ds = AuditLoggingTestHost.CreateDataService(fsql);
        var now = DateTime.UtcNow;
        await InsertAtAsync(ds, "alice", "OrderService", now.AddMinutes(-10), success: true, durationMs: 50);   // 成功 ×3
        await InsertAtAsync(ds, "bob", "OrderService", now.AddMinutes(-10), success: true, durationMs: 30);
        await InsertAtAsync(ds, "carol", "OrderService", now.AddMinutes(-10), success: true, durationMs: 100);
        await InsertAtAsync(ds, "dave", "PaymentService", now.AddMinutes(-10), success: false, durationMs: 200); // 失败 ×2
        await InsertAtAsync(ds, "erin", "PaymentService", now.AddMinutes(-10), success: false, durationMs: 400);

        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql);
        var stats = await analytics.GetStatsAsync();

        Assert.Equal(5, stats.Total);
        Assert.Equal(3, stats.Succeeded);
        Assert.Equal(2, stats.Failed);
        // (50+30+100+200+400)/5 = 156 ms
        Assert.Equal(156.0, stats.AvgDurationMs, 2);
        Assert.Equal(400, stats.MaxDurationMs);
    }

    [Fact]
    public async Task GetStatsAsync_RespectsWindow()
    {
        using var fsql = CreateInMemoryFreeSql();
        var ds = AuditLoggingTestHost.CreateDataService(fsql);
        var now = DateTime.UtcNow;
        await InsertAtAsync(ds, "alice", "OrderService", now.AddMinutes(-10), success: true, durationMs: 50);   // 窗内 ×2
        await InsertAtAsync(ds, "bob", "OrderService", now.AddMinutes(-10), success: false, durationMs: 30);
        await InsertAtAsync(ds, "carol", "PaymentService", now.AddHours(-2), success: true, durationMs: 999);    // 窗外 ×1（失败会暴露 Avg/Max）

        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql);

        var windowed = await analytics.GetStatsAsync(window: TimeSpan.FromMinutes(30));
        Assert.Equal(2, windowed.Total);
        Assert.Equal(1, windowed.Succeeded);
        Assert.Equal(1, windowed.Failed);
        Assert.Equal(40.0, windowed.AvgDurationMs, 2);   // (50+30)/2 —— 窗外 999ms 不计入
        Assert.Equal(50, windowed.MaxDurationMs);

        var all = await analytics.GetStatsAsync();
        Assert.Equal(3, all.Total);
        Assert.Equal(999, all.MaxDurationMs);
    }

    [Fact]
    public async Task GetStatsAsync_EmptyDatabase_ReturnsZeros()
    {
        using var fsql = CreateInMemoryFreeSql();
        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql);

        var stats = await analytics.GetStatsAsync();
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.Succeeded);
        Assert.Equal(0, stats.Failed);
        Assert.Equal(0, stats.AvgDurationMs);
        Assert.Equal(0, stats.MaxDurationMs);
    }

    // ── ③ 保留天数清理 ──

    [Fact]
    public async Task CleanupExpiredAsync_DeletesExpired_KeepsRecent()
    {
        using var fsql = CreateInMemoryFreeSql();
        var ds = AuditLoggingTestHost.CreateDataService(fsql);
        var now = DateTime.UtcNow;
        var expiredA = await InsertAtAsync(ds, "alice", "OrderService", now.AddDays(-100));  // 过期（RetentionDays=90）
        var expiredB = await InsertAtAsync(ds, "bob", "PaymentService", now.AddDays(-200));  // 过期
        var recentA = await InsertAtAsync(ds, "carol", "InventoryService", now.AddMinutes(-5)); // 保留
        var recentB = await InsertAtAsync(ds, "dave", "ShippingService", now);                  // 保留

        // 默认 Options（RetentionDays=90、CleanupBatchSize=500）
        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql);
        var deleted = await analytics.CleanupExpiredAsync();

        Assert.Equal(2, deleted);

        var query = AuditLoggingTestHost.CreateQueryService(fsql);
        var remaining = await query.GetListAsync(new AuditLogQueryInput());
        var remainingIds = remaining.Items.Select(i => i.Id).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { recentA.Id, recentB.Id }.OrderBy(i => i), remainingIds);   // Id 集合断言（不比较 DateTime）
        Assert.Equal(2, remaining.Total);
        Assert.Contains(remaining.Items, i => i.UserName == "carol");
        Assert.Contains(remaining.Items, i => i.UserName == "dave");
    }

    [Fact]
    public async Task CleanupExpiredAsync_BatchesUntilEmpty()
    {
        using var fsql = CreateInMemoryFreeSql();
        var ds = AuditLoggingTestHost.CreateDataService(fsql);
        var now = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
            await InsertAtAsync(ds, $"user{i}", $"Service{i}", now.AddDays(-100));

        // 单批仅 2 条 → 验证分批循环（2+2+1=5 全部清完）
        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql,
            new AuditLoggingOptions { CleanupBatchSize = 2 });
        var deleted = await analytics.CleanupExpiredAsync();

        Assert.Equal(5, deleted);

        var query = AuditLoggingTestHost.CreateQueryService(fsql);
        Assert.Equal(0, await query.CountAsync(new AuditLogQueryInput()));
    }

    [Fact]
    public async Task CleanupExpiredAsync_NoExpired_ReturnsZero()
    {
        using var fsql = CreateInMemoryFreeSql();
        var ds = AuditLoggingTestHost.CreateDataService(fsql);
        await InsertAtAsync(ds, "alice", "OrderService", DateTime.UtcNow);   // 仅近期记录

        var analytics = AuditLoggingTestHost.CreateAnalyticsService(fsql);
        Assert.Equal(0, await analytics.CleanupExpiredAsync());

        var query = AuditLoggingTestHost.CreateQueryService(fsql);
        Assert.Equal(1, await query.CountAsync(new AuditLogQueryInput()));
    }

    // ── ④ 异常静默 ──

    [Fact]
    public async Task AnalyticsService_ExceptionSilent()
    {
        // Dispose 后操作 → 异常静默（Warning + 空结果/0，不抛异常、不阻断消费方）
        var fsql = CreateInMemoryFreeSql();
        var logger = new FakeLogger<AuditLogAnalyticsService>();
        var analytics = new AuditLogAnalyticsService(
            AuditLoggingTestHost.CreateDataService(fsql),
            new OptionsWrapper<AuditLoggingOptions>(new AuditLoggingOptions()),
            logger);

        fsql.Dispose();

        Assert.Empty(await analytics.GetTopServicesAsync());
        Assert.Empty(await analytics.GetTopUsersAsync());
        var stats = await analytics.GetStatsAsync();
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, await analytics.CleanupExpiredAsync());
        Assert.NotEmpty(logger.Warnings);
    }

    /// <summary>简化 ILogger 桩：捕获 Warning 日志。</summary>
    private sealed class FakeLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}