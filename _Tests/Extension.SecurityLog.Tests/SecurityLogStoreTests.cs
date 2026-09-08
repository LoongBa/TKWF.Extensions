using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// SecurityLogStore 测试——使用 SQLite 内存库验证真实写入 + 异常静默 + 只增不改语义。
/// </summary>
public class SecurityLogStoreTests
{
    [Fact]
    public async Task SaveAsync_NormalInsert_PersistsAllFields()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);

        var entry = new SecurityLogEntry(
            EventType: "Login",
            EventCategory: "Authentication",
            UserName: "alice",
            UserId: 100,
            IpAddress: "203.0.113.7",
            UserAgent: "Mozilla/5.0",
            Result: "Success",
            Detail: null,
            CorrelationId: "corr-1");

        await store.SaveAsync(entry);

        // 经 DataService 业务方法回查（红线：断言不经裸 fsql.Select）
        var dataService = SecurityLogTestHost.CreateDataService(fsql);
        var saved = await dataService.EntityGetAsync(e => e.CorrelationId == "corr-1", CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("Login", saved!.EventType);
        Assert.Equal("Authentication", saved.EventCategory);
        Assert.Equal("alice", saved.UserName);
        Assert.Equal(100, saved.UserId);
        Assert.Equal("203.0.113.7", saved.IpAddress);
        Assert.Equal("Mozilla/5.0", saved.UserAgent);
        Assert.Equal("Success", saved.Result);
        Assert.Null(saved.Detail);
        Assert.True(saved.CreateTime > DateTime.MinValue);
    }

    [Fact]
    public async Task SaveAsync_FailedEntry_DetailPersisted()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);

        await store.SaveAsync(new SecurityLogEntry(
            EventType: "Login",
            EventCategory: "Authentication",
            UserName: "bob",
            UserId: null,
            IpAddress: null,
            UserAgent: null,
            Result: "Failed",
            Detail: "密码错误",
            CorrelationId: null));

        var dataService = SecurityLogTestHost.CreateDataService(fsql);
        var all = await dataService.EntitySelectAsync(predicate: null, ct: CancellationToken.None);
        var saved = Assert.Single(all);
        Assert.Equal("Failed", saved.Result);
        Assert.Equal("密码错误", saved.Detail);
    }

    [Fact]
    public async Task SaveAsync_NullEntry_DoesNotThrow_NoRows()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);

        await store.SaveAsync(null!);

        var dataService = SecurityLogTestHost.CreateDataService(fsql);
        var all = await dataService.EntitySelectAsync(predicate: null, ct: CancellationToken.None);
        Assert.Empty(all);
    }

    [Fact]
    public async Task SaveAsync_DatabaseFailure_LogsWarning_DoesNotThrow()
    {
        // Dispose 后写入 → 落库失败；Store 异常静默（不阻断认证流程）
        var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var logger = new FakeLogger<SecurityLogStore>();
        var store = new SecurityLogStore(
            SecurityLogTestHost.CreateDataService(fsql), logger);

        fsql.Dispose();

        await store.SaveAsync(new SecurityLogEntry(
            EventType: "Login",
            EventCategory: "Authentication",
            UserName: "alice",
            UserId: null,
            IpAddress: null,
            UserAgent: null,
            Result: "Success",
            Detail: null,
            CorrelationId: "x"));

        Assert.NotEmpty(logger.Warnings);   // Warning 已记录
        Assert.True(true);                  // 静默语义保留（不抛异常）
    }

    [Fact]
    public async Task SaveAsync_MultipleEntries_AllAppended()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);

        for (int i = 0; i < 5; i++)
        {
            await store.SaveAsync(new SecurityLogEntry(
                EventType: "Login",
                EventCategory: "Authentication",
                UserName: $"user{i}",
                UserId: i,
                IpAddress: null,
                UserAgent: null,
                Result: "Success",
                Detail: null,
                CorrelationId: null));
        }

        var dataService = SecurityLogTestHost.CreateDataService(fsql);
        var all = await dataService.EntitySelectAsync(predicate: null, ct: CancellationToken.None);
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public void Constructor_NullDataService_Throws()
    {
        var logger = new FakeLogger<SecurityLogStore>();
        Assert.Throws<ArgumentNullException>(() => new SecurityLogStore(null!, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        Assert.Throws<ArgumentNullException>(
            () => new SecurityLogStore(SecurityLogTestHost.CreateDataService(fsql), null!));
    }
}
