using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// AccountLockoutEntityDataService 测试（A5 Store-debt cleanup 后）——锁定记录 CRUD。
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

    private static AccountLockoutEntityDataService CreateStore(IFreeSql fsql)
        => AccountTestHost.CreateLockoutDataService(fsql);

    [Fact]
    public async Task SaveAndGet_RoundTrip()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        var record = new AccountLockoutEntity { UserName = "alice", FailedCount = 2 };
        await store.UpsertAsync(record, CancellationToken.None);

        var loaded = await store.GetByUserNameAsync("alice", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.FailedCount);
    }

    [Fact]
    public async Task Get_NotExists_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        var loaded = await store.GetByUserNameAsync("nobody", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAsync_ExistingRecord_Updates()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var record = new AccountLockoutEntity { UserName = "alice", FailedCount = 1 };
        await store.UpsertAsync(record, CancellationToken.None);

        // 复用同一记录对象递增计数 → 触发更新
        record.FailedCount = 3;
        await store.UpsertAsync(record, CancellationToken.None);

        var loaded = await store.GetByUserNameAsync("alice", CancellationToken.None);
        Assert.Equal(3, loaded!.FailedCount);
        Assert.Equal(1, fsql.Select<AccountLockoutEntity>().Count());
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        await store.UpsertAsync(new AccountLockoutEntity { UserName = "alice", FailedCount = 1 }, CancellationToken.None);

        await store.DeleteByUserNameAsync("alice", CancellationToken.None);

        Assert.Null(await store.GetByUserNameAsync("alice", CancellationToken.None));
    }
}
