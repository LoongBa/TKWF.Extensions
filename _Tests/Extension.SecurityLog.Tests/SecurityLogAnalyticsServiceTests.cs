using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// SecurityLogAnalyticsService（v0.2.0）测试——失败次数 TopN 聚合（ByUser/ByIp + 时间窗口 + 空白维度跳过）+
/// 保留天数清理（RetentionDays/分批循环/无过期零删除）+ 异常静默（DataService 异常 → 空结果/Warning 不抛）。
/// <para>沿用 SecurityLogTestHost SQLite 内存 + 真实 FreeSqlEntityDAC 模式；时间断言用 Id 集合/计数相对容错
/// （SQLite UTC 存储陷阱：UTC 12:00 存 → 可能 Unspecified 读出——勿精确比较 DateTime）。</para>
/// </summary>
public class SecurityLogAnalyticsServiceTests
{
    /// <summary>准备一条登录事件（CreateTime 由 SaveAsync 落库时取 UtcNow）。</summary>
    private static SecurityLogEntry Login(string userName, long? userId, string ip, string result, string? detail)
        => new("Login", "Authentication", userName, userId, ip, null, result, detail, null);

    /// <summary>经 DataService 显式 CreateTime 落库（清理/窗口测试需精确控制时间；原始 UTC 值——同 QueryServiceTests 先例）。</summary>
    private static async Task<SecurityLogEntity> InsertAtAsync(
        SecurityLogEntityDataService ds, string userName, string ip, string result, DateTime createTimeUtc)
        => await ds.EntityCreateAsync(new SecurityLogEntity
        {
            EventType = "Login",
            EventCategory = "Authentication",
            UserName = userName,
            IpAddress = ip,
            Result = result,
            CreateTime = createTimeUtc,
        });

    // ── ① 失败次数 TopN 聚合 ──

    [Fact]
    public async Task GetTopFailedUsersAsync_ReturnsTopNByFailedCount()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Failed", "密码错误"));   // alice: 3 Failed
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Failed", "密码错误"));
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Failed", "密码错误"));
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Success", null));       // Success 不计入
        await store.SaveAsync(Login("bob", 2, "10.0.0.2", "Failed", "密码错误"));     // bob: 2
        await store.SaveAsync(Login("bob", 2, "10.0.0.2", "Failed", "密码错误"));
        await store.SaveAsync(Login("carol", 3, "10.0.0.3", "Failed", "密码错误"));   // carol: 4
        await store.SaveAsync(Login("carol", 3, "10.0.0.3", "Failed", "密码错误"));
        await store.SaveAsync(Login("carol", 3, "10.0.0.3", "Failed", "密码错误"));
        await store.SaveAsync(Login("carol", 3, "10.0.0.3", "Failed", "密码错误"));

        var analytics = SecurityLogTestHost.CreateAnalyticsService(fsql);

        var top2 = await analytics.GetTopFailedUsersAsync(topN: 2);
        Assert.Equal(2, top2.Count);
        Assert.Equal("carol", top2[0].Dimension);
        Assert.Equal(4, top2[0].Count);
        Assert.Equal("alice", top2[1].Dimension);
        Assert.Equal(3, top2[1].Count);   // Success 不计数——alice 若含 Success 会是 4

        var top5 = await analytics.GetTopFailedUsersAsync(topN: 5);
        Assert.Equal(3, top5.Count);      // 仅 3 个有失败记录的用户
        Assert.Equal("bob", top5[2].Dimension);
        Assert.Equal(2, top5[2].Count);
    }

    [Fact]
    public async Task GetTopFailedUsersAsync_RespectsWindow()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var ds = SecurityLogTestHost.CreateDataService(fsql);
        var now = DateTime.UtcNow;
        await InsertAtAsync(ds, "alice", "10.0.0.1", "Failed", now.AddMinutes(-10));   // 窗内
        await InsertAtAsync(ds, "alice", "10.0.0.1", "Failed", now.AddMinutes(-10));
        await InsertAtAsync(ds, "alice", "10.0.0.1", "Failed", now.AddMinutes(-10));
        await InsertAtAsync(ds, "bob", "10.0.0.2", "Failed", now.AddHours(-2));        // 窗外
        await InsertAtAsync(ds, "bob", "10.0.0.2", "Failed", now.AddHours(-2));

        var analytics = SecurityLogTestHost.CreateAnalyticsService(fsql);

        // 30 分钟窗口 → 仅 alice 计入（bob 在 2 小时前被过滤）
        var windowed = await analytics.GetTopFailedUsersAsync(topN: 10, window: TimeSpan.FromMinutes(30));
        var alice = Assert.Single(windowed);
        Assert.Equal("alice", alice.Dimension);
        Assert.Equal(3, alice.Count);

        // 全量（window: null）→ 两用户都计入
        var all = await analytics.GetTopFailedUsersAsync(topN: 10);
        Assert.Equal(2, all.Count);
        Assert.Equal(3, all.Single(s => s.Dimension == "alice").Count);
        Assert.Equal(2, all.Single(s => s.Dimension == "bob").Count);
    }

    [Fact]
    public async Task GetTopFailedIpsAsync_ExcludesNullEmptyIp()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Failed", null));   // IP 1: 2 次
        await store.SaveAsync(Login("bob", 2, "10.0.0.1", "Failed", null));
        await store.SaveAsync(Login("carol", 3, "10.0.0.2", "Failed", null));   // IP 2: 1 次
        await store.SaveAsync(Login("dave", 4, null, "Failed", null));          // IP null: 不应计入
        await store.SaveAsync(Login("erin", 5, "", "Failed", null));            // IP 空串: 不应计入

        var analytics = SecurityLogTestHost.CreateAnalyticsService(fsql);
        var result = await analytics.GetTopFailedIpsAsync(topN: 10);

        Assert.Equal(2, result.Count);
        Assert.Equal("10.0.0.1", result[0].Dimension);
        Assert.Equal(2, result[0].Count);
        Assert.Equal("10.0.0.2", result[1].Dimension);
        Assert.Equal(1, result[1].Count);
        Assert.All(result, s => Assert.False(string.IsNullOrWhiteSpace(s.Dimension)));
        Assert.Equal(3, result.Sum(s => s.Count));   // null/空 IP 的 2 条被跳过
    }

    [Fact]
    public async Task GetTopFailedUsersAsync_NoData_ReturnsEmpty()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var analytics = SecurityLogTestHost.CreateAnalyticsService(fsql);

        Assert.Empty(await analytics.GetTopFailedUsersAsync());
        Assert.Empty(await analytics.GetTopFailedIpsAsync());
    }

    // ── ② 保留天数清理 ──

    [Fact]
    public async Task CleanupExpiredAsync_DeletesExpired_KeepsRecent()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var ds = SecurityLogTestHost.CreateDataService(fsql);
        var now = DateTime.UtcNow;
        var expiredA = await InsertAtAsync(ds, "alice", "10.0.0.1", "Failed", now.AddDays(-100));  // 过期（RetentionDays=90）
        var expiredB = await InsertAtAsync(ds, "bob", "10.0.0.2", "Failed", now.AddDays(-200));     // 过期
        var recentA = await InsertAtAsync(ds, "carol", "10.0.0.3", "Success", now.AddMinutes(-5));  // 保留
        var recentB = await InsertAtAsync(ds, "dave", "10.0.0.4", "Failed", now);                   // 保留

        // 默认 Options（RetentionDays=90、CleanupBatchSize=500）
        var analytics = SecurityLogTestHost.CreateAnalyticsService(fsql);
        var deleted = await analytics.CleanupExpiredAsync();

        Assert.Equal(2, deleted);

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        var remaining = await query.GetListAsync(new SecurityLogQueryInput());
        var remainingIds = remaining.Items.Select(i => i.Id).OrderBy(i => i).ToArray();
        Assert.Equal(new[] { recentA.Id, recentB.Id }.OrderBy(i => i), remainingIds);   // Id 集合断言（不比较 DateTime）
        Assert.Equal(2, remaining.Total);
        Assert.Contains(remaining.Items, i => i.UserName == "carol");
        Assert.Contains(remaining.Items, i => i.UserName == "dave");
    }

    [Fact]
    public async Task CleanupExpiredAsync_BatchesUntilEmpty()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var ds = SecurityLogTestHost.CreateDataService(fsql);
        var now = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
            await InsertAtAsync(ds, $"user{i}", $"10.0.0.{i}", "Failed", now.AddDays(-100));

        // 单批仅 2 条 → 验证分批循环（2+2+1=5 全部清完）
        var analytics = SecurityLogTestHost.CreateAnalyticsService(fsql,
            new SecurityLoggingOptions { CleanupBatchSize = 2 });
        var deleted = await analytics.CleanupExpiredAsync();

        Assert.Equal(5, deleted);

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        Assert.Equal(0, await query.CountAsync(new SecurityLogQueryInput()));
    }

    [Fact]
    public async Task CleanupExpiredAsync_NoExpired_ReturnsZero()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var ds = SecurityLogTestHost.CreateDataService(fsql);
        await InsertAtAsync(ds, "alice", "10.0.0.1", "Success", DateTime.UtcNow);   // 仅近期记录

        var analytics = SecurityLogTestHost.CreateAnalyticsService(fsql);
        Assert.Equal(0, await analytics.CleanupExpiredAsync());

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        Assert.Equal(1, await query.CountAsync(new SecurityLogQueryInput()));
    }

    // ── ③ 异常静默 ──

    [Fact]
    public async Task AnalyticsService_ExceptionSilent()
    {
        // Dispose 后操作 → 异常静默（Warning + 空结果/0，不抛异常、不阻断消费方）
        var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var logger = new FakeLogger<SecurityLogAnalyticsService>();
        var analytics = new SecurityLogAnalyticsService(
            SecurityLogTestHost.CreateDataService(fsql),
            new Microsoft.Extensions.Options.OptionsWrapper<SecurityLoggingOptions>(new SecurityLoggingOptions()),
            logger);

        fsql.Dispose();

        Assert.Empty(await analytics.GetTopFailedUsersAsync());
        Assert.Empty(await analytics.GetTopFailedIpsAsync());
        Assert.Equal(0, await analytics.CleanupExpiredAsync());
        Assert.NotEmpty(logger.Warnings);
    }
}