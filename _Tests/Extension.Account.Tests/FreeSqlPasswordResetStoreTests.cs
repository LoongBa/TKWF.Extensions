using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// PasswordResetCodeEntityDataService 测试（A5 Store-debt cleanup 后）——重置码 CRUD + 标记使用。
/// </summary>
public class FreeSqlPasswordResetStoreTests
{
    private static IFreeSql CreateFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<PasswordResetCodeEntity>();
        return fsql;
    }

    private static PasswordResetCodeEntityDataService CreateStore(IFreeSql fsql)
        => AccountTestHost.CreatePasswordResetDataService(fsql);

    [Fact]
    public async Task SaveAndGet_RoundTrip()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        var record = new PasswordResetCodeEntity
        {
            UserName = "alice",
            ResetCode = "ABC12345",
            ExpireTime = DateTime.Now.AddMinutes(30)
        };
        await store.SaveAsync(record, CancellationToken.None);

        var loaded = await store.GetByUserNameAndCodeAsync("alice", "ABC12345", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.False(loaded!.IsUsed);
        Assert.Equal(record.Id, loaded.Id);
    }

    [Fact]
    public async Task Get_WrongCode_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var record = new PasswordResetCodeEntity
        {
            UserName = "alice",
            ResetCode = "ABC12345",
            ExpireTime = DateTime.Now.AddMinutes(30)
        };
        await store.SaveAsync(record, CancellationToken.None);

        var loaded = await store.GetByUserNameAndCodeAsync("alice", "WRONG000", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Get_WrongUser_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var record = new PasswordResetCodeEntity
        {
            UserName = "alice",
            ResetCode = "ABC12345",
            ExpireTime = DateTime.Now.AddMinutes(30)
        };
        await store.SaveAsync(record, CancellationToken.None);

        var loaded = await store.GetByUserNameAndCodeAsync("bob", "ABC12345", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task MarkUsedAsync_SetsIsUsed()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var record = new PasswordResetCodeEntity
        {
            UserName = "alice",
            ResetCode = "ABC12345",
            ExpireTime = DateTime.Now.AddMinutes(30)
        };
        await store.SaveAsync(record, CancellationToken.None);

        await store.MarkUsedAsync(record.Id, CancellationToken.None);

        var loaded = await store.GetByUserNameAndCodeAsync("alice", "ABC12345", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsUsed);
    }
}
