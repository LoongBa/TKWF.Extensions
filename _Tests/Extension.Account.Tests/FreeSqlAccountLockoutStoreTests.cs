using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// FreeSqlAccountLockoutStore 测试——锁定记录 CRUD + 异常静默。
/// </summary>
public class FreeSqlAccountLockoutStoreTests
{
    private static IFreeSql CreateFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<AccountLockoutEntity>();
        return fsql;
    }

    private static AccountLockoutStore CreateStore(IFreeSql fsql)
        => AccountTestHost.CreateLockoutStore(fsql);

    [Fact]
    public async Task SaveAndGet_RoundTrip()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        var record = new AccountLockoutEntity { UserName = "alice", FailedCount = 2 };
        await store.SaveAsync(record, CancellationToken.None);

        var loaded = await store.GetAsync("alice", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.FailedCount);
    }

    [Fact]
    public async Task Get_NotExists_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        var loaded = await store.GetAsync("nobody", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAsync_ExistingRecord_Updates()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var record = new AccountLockoutEntity { UserName = "alice", FailedCount = 1 };
        await store.SaveAsync(record, CancellationToken.None);

        // 复用同一记录对象递增计数 → 触发更新
        record.FailedCount = 3;
        await store.SaveAsync(record, CancellationToken.None);

        var loaded = await store.GetAsync("alice", CancellationToken.None);
        Assert.Equal(3, loaded!.FailedCount);
        // Store 无列表/计数业务方法（仅 Get/Save/Delete）——"更新不产生重复记录"无法经业务方法表达，
        // 保留直查校验 Upsert 语义（同用户名二次 Save 仍为单行）。
        Assert.Equal(1, fsql.Select<AccountLockoutEntity>().Count()); // 无业务方法覆盖（Store 无 count 接口），保留直查
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        await store.SaveAsync(new AccountLockoutEntity { UserName = "alice", FailedCount = 1 }, CancellationToken.None);

        await store.DeleteAsync("alice", CancellationToken.None);

        Assert.Null(await store.GetAsync("alice", CancellationToken.None));
    }

    [Fact]
    public async Task Operations_FailSilently_OnNullEntity()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        await store.SaveAsync(null!, CancellationToken.None);

        Assert.Null(await store.GetAsync("alice", CancellationToken.None));
    }
}