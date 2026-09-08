using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// SecurityLogQueryService 测试——分页/过滤（UserName/IpAddress/EventType/Result/时间）+ Count +
/// 列表 DTO 不含 Detail（GetDetailAsync 含）+ Take 上限 200（D5）。
/// </summary>
public class SecurityLogQueryServiceTests
{
    /// <summary>准备一条安全事件。</summary>
    private static SecurityLogEntry Login(string userName, long? userId, string ip, string result, string? detail)
        => new("Login", "Authentication", userName, userId, ip, null, result, detail, null);

    [Fact]
    public async Task GetListAsync_NoFilter_ReturnsAllOrderedByCreateTimeDesc()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Success", null));
        await store.SaveAsync(Login("bob", 2, "10.0.0.2", "Failed", "密码错误"));
        await store.SaveAsync(Login("carol", 3, "10.0.0.3", "Failed", "账户已锁定"));

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        var result = await query.GetListAsync(new SecurityLogQueryInput());

        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal("carol", result.Items[0].UserName);   // 最新在前
    }

    [Fact]
    public async Task GetListAsync_FilterByUserName_ContainsMatch()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Success", null));
        await store.SaveAsync(Login("bob", 2, "10.0.0.2", "Failed", "密码错误"));

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        var result = await query.GetListAsync(new SecurityLogQueryInput { UserName = "ali" });

        Assert.Equal(1, result.Total);
        Assert.Equal("alice", Assert.Single(result.Items).UserName);
    }

    [Fact]
    public async Task GetListAsync_FilterByIpAddress_ContainsMatch()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        await store.SaveAsync(Login("alice", 1, "203.0.113.7", "Success", null));
        await store.SaveAsync(Login("bob", 2, "198.51.100.9", "Failed", "密码错误"));

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        var result = await query.GetListAsync(new SecurityLogQueryInput { IpAddress = "203.0.113" });

        Assert.Equal(1, result.Total);
        Assert.Equal("alice", Assert.Single(result.Items).UserName);
    }

    [Fact]
    public async Task GetListAsync_FilterByEventTypeAndResult_ExactMatch()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Success", null));
        await store.SaveAsync(Login("bob", 2, "10.0.0.2", "Failed", "密码错误"));
        await store.SaveAsync(new SecurityLogEntry("Logout", "Authentication", "alice", 1, null, null, "Success", null, null));

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        var result = await query.GetListAsync(new SecurityLogQueryInput { EventType = "Login", Result = "Failed" });

        Assert.Equal(1, result.Total);
        Assert.Equal("bob", Assert.Single(result.Items).UserName);
    }

    [Fact]
    public async Task GetListAsync_FilterByTimeRange()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        // 经 DataService 显式 CreateTime 落库（原始 UTC 秒整值——对齐 AuditLogging 时间过滤测试先例；
        // 规避 FreeSql SQLite "存储带 Z 后缀 / 回读转本地" 的不对称比较，边界秒不落歧义区）
        var ds = SecurityLogTestHost.CreateDataService(fsql);
        var t1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 1, 1, 10, 2, 0, DateTimeKind.Utc);
        await ds.EntityCreateAsync(new SecurityLogEntity { EventType = "Login", EventCategory = "Authentication", UserName = "alice", Result = "Success", CreateTime = t1 });
        await ds.EntityCreateAsync(new SecurityLogEntity { EventType = "Login", EventCategory = "Authentication", UserName = "bob", Result = "Failed", CreateTime = t2 });
        await ds.EntityCreateAsync(new SecurityLogEntity { EventType = "Login", EventCategory = "Authentication", UserName = "carol", Result = "Success", CreateTime = t3 });

        var query = SecurityLogTestHost.CreateQueryService(fsql);

        // 闭区间 [10:00:30, 10:01:30] → 仅 bob（10:01:00）落在窗内
        var windowed = await query.GetListAsync(new SecurityLogQueryInput
        {
            FromUtc = new DateTime(2026, 1, 1, 10, 0, 30, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 1, 10, 1, 30, DateTimeKind.Utc),
        });
        Assert.Equal(1, windowed.Total);
        Assert.Equal("bob", Assert.Single(windowed.Items).UserName);

        // 仅下界（>= t1）→ 全部 3 条
        var ge = await query.GetListAsync(new SecurityLogQueryInput
        {
            FromUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
        });
        Assert.Equal(3, ge.Total);

        // 仅上界（<= 10:01:59）→ alice + bob
        var le = await query.GetListAsync(new SecurityLogQueryInput
        {
            ToUtc = new DateTime(2026, 1, 1, 10, 1, 59, DateTimeKind.Utc),
        });
        Assert.Equal(2, le.Total);
    }

    [Fact]
    public async Task GetListAsync_Paging_SkipTakeApplied()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        for (int i = 0; i < 5; i++)
            await store.SaveAsync(Login($"user{i}", i, $"10.0.0.{i}", "Success", null));

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        var result = await query.GetListAsync(new SecurityLogQueryInput { Skip = 2, Take = 2 });

        Assert.Equal(5, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("user2", result.Items[0].UserName);   // 按 CreateTime 倒序：user4, user3, user2, user1, user0
        Assert.Equal("user1", result.Items[1].UserName);
    }

    [Fact]
    public async Task GetListAsync_ListItemDto_DoesNotContainDetail()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Failed", "密码错误"));

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        var result = await query.GetListAsync(new SecurityLogQueryInput());

        var item = Assert.Single(result.Items);
        Assert.Equal("Login", item.EventType);
        Assert.Equal("Authentication", item.EventCategory);
        Assert.Equal("alice", item.UserName);
        Assert.Equal(1, item.UserId);
        Assert.Equal("10.0.0.1", item.IpAddress);
        Assert.Equal("Failed", item.Result);
        // 安全决策 D5：列表 DTO 反射强断言——确无 Detail 属性
        Assert.Null(typeof(SecurityLogListItemDto).GetProperty("Detail"));
    }

    [Fact]
    public async Task GetDetailAsync_ContainsFullDetail()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        await store.SaveAsync(new SecurityLogEntry(
            "Login", "Authentication", "bob", null, "198.51.100.9", "Mozilla/5.0", "Failed",
            "账户已锁定，请联系管理员解锁", "corr-9"));

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        var result = await query.GetListAsync(new SecurityLogQueryInput { UserName = "bob" });
        var id = Assert.Single(result.Items).Id;

        var detail = await query.GetDetailAsync(id);
        Assert.NotNull(detail);
        Assert.Equal("账户已锁定，请联系管理员解锁", detail!.Detail);   // 详情含全文
        Assert.Equal("Mozilla/5.0", detail.UserAgent);
        Assert.Equal("corr-9", detail.CorrelationId);
        Assert.Equal("Failed", detail.Result);
    }

    [Fact]
    public async Task GetDetailAsync_NotFound_ReturnsNull()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var query = SecurityLogTestHost.CreateQueryService(fsql);
        Assert.Null(await query.GetDetailAsync(99999));
    }

    [Fact]
    public async Task CountAsync_MatchesFilter()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        await store.SaveAsync(Login("alice", 1, "10.0.0.1", "Success", null));
        await store.SaveAsync(Login("bob", 2, "10.0.0.2", "Failed", "密码错误"));
        await store.SaveAsync(Login("bob", 3, "10.0.0.3", "Failed", "密码错误"));

        var query = SecurityLogTestHost.CreateQueryService(fsql);
        Assert.Equal(3, await query.CountAsync(new SecurityLogQueryInput()));
        Assert.Equal(2, await query.CountAsync(new SecurityLogQueryInput { UserName = "bob" }));
        Assert.Equal(2, await query.CountAsync(new SecurityLogQueryInput { Result = "Failed" }));
        Assert.Equal(0, await query.CountAsync(new SecurityLogQueryInput { EventType = "Lockout" }));
    }

    [Fact]
    public async Task Take_ClampedToMax200_AndDefault50()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);
        for (int i = 0; i < 60; i++)
            await store.SaveAsync(Login($"user{i}", i, $"10.0.0.{i % 10}", "Success", null));

        var query = SecurityLogTestHost.CreateQueryService(fsql);

        // Take 默认 50
        var defaultResult = await query.GetListAsync(new SecurityLogQueryInput());
        Assert.Equal(50, defaultResult.Items.Count);

        // Take 上限 200
        var clamped = await query.GetListAsync(new SecurityLogQueryInput { Take = 1000 });
        Assert.Equal(60, clamped.Items.Count);

        // Take=0 → 默认 50
        var zero = await query.GetListAsync(new SecurityLogQueryInput { Take = 0 });
        Assert.Equal(50, zero.Items.Count);
    }

    [Fact]
    public async Task QueryFailure_LogsWarning_ReturnsEmpty()
    {
        // Dispose 后查询 → 异常静默（Warning + 空结果，不阻断消费方）
        var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var logger = new FakeLogger<SecurityLogQueryService>();
        var query = new SecurityLogQueryService(SecurityLogTestHost.CreateDataService(fsql), logger);

        fsql.Dispose();

        var result = await query.GetListAsync(new SecurityLogQueryInput());
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
        Assert.Equal(0, await query.CountAsync(new SecurityLogQueryInput()));
        Assert.NotEmpty(logger.Warnings);
    }
}
