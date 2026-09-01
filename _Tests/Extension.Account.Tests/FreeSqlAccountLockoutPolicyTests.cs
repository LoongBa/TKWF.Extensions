using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.AuthController;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// FreeSqlAccountLockoutPolicy 测试——锁定判定/失败递增到阈值/成功重置/解锁/过期自动解锁。
/// </summary>
public class FreeSqlAccountLockoutPolicyTests
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

    private static FreeSqlAccountLockoutPolicy CreatePolicy(IFreeSql fsql, AccountOptions? options = null)
    {
        var store = new FreeSqlAccountLockoutStore(fsql, NullLogger<FreeSqlAccountLockoutStore>.Instance);
        return new FreeSqlAccountLockoutPolicy(
            store,
            Options.Create(options ?? new AccountOptions()),
            NullLogger<FreeSqlAccountLockoutPolicy>.Instance);
    }

    [Fact]
    public async Task IsLocked_NoRecord_ReturnsFalse()
    {
        var fsql = CreateFreeSql();
        var policy = CreatePolicy(fsql);

        var locked = await policy.IsLockedAsync("alice", CancellationToken.None);

        Assert.False(locked);
    }

    [Fact]
    public async Task FailedLogin_BelowThreshold_NotLocked()
    {
        var fsql = CreateFreeSql();
        var policy = CreatePolicy(fsql, new AccountOptions { MaxFailedAttempts = 5 });

        await policy.OnFailedLoginAsync("alice", CancellationToken.None);
        await policy.OnFailedLoginAsync("alice", CancellationToken.None);

        var locked = await policy.IsLockedAsync("alice", CancellationToken.None);
        Assert.False(locked);
        var rec = fsql.Select<AccountLockoutEntity>().First();
        Assert.Equal(2, rec.FailedCount);
        Assert.Null(rec.LockoutEnd);
    }

    [Fact]
    public async Task FailedLogin_ReachesThreshold_Locks()
    {
        var fsql = CreateFreeSql();
        var policy = CreatePolicy(fsql, new AccountOptions { MaxFailedAttempts = 3, DefaultLockoutMinutes = 15 });

        for (var i = 0; i < 3; i++)
            await policy.OnFailedLoginAsync("alice", CancellationToken.None);

        var locked = await policy.IsLockedAsync("alice", CancellationToken.None);
        Assert.True(locked);
        var rec = fsql.Select<AccountLockoutEntity>().First();
        Assert.NotNull(rec.LockoutEnd);
        Assert.True(rec.LockoutEnd > DateTime.Now);
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsLockout()
    {
        var fsql = CreateFreeSql();
        var policy = CreatePolicy(fsql, new AccountOptions { MaxFailedAttempts = 3 });
        for (var i = 0; i < 4; i++)
            await policy.OnFailedLoginAsync("alice", CancellationToken.None);
        Assert.True(await policy.IsLockedAsync("alice", CancellationToken.None));

        await policy.OnSuccessfulLoginAsync("alice", CancellationToken.None);

        Assert.False(await policy.IsLockedAsync("alice", CancellationToken.None));
        Assert.Equal(0, fsql.Select<AccountLockoutEntity>().Count());
    }

    [Fact]
    public async Task Unlock_DeletesRecord()
    {
        var fsql = CreateFreeSql();
        var policy = CreatePolicy(fsql, new AccountOptions { MaxFailedAttempts = 2 });
        await policy.OnFailedLoginAsync("alice", CancellationToken.None);
        await policy.OnFailedLoginAsync("alice", CancellationToken.None);
        Assert.True(await policy.IsLockedAsync("alice", CancellationToken.None));

        await policy.UnlockAsync("alice", CancellationToken.None);

        Assert.False(await policy.IsLockedAsync("alice", CancellationToken.None));
        Assert.Equal(0, fsql.Select<AccountLockoutEntity>().Count());
    }

    [Fact]
    public async Task IsLocked_ExpiredLockout_ReturnsFalse()
    {
        var fsql = CreateFreeSql();
        var policy = CreatePolicy(fsql);
        await fsql.Insert(new AccountLockoutEntity
        {
            UserName = "alice",
            FailedCount = 1,
            LockoutEnd = DateTime.Now.AddMinutes(-1) // 已过期
        }).ExecuteAffrowsAsync();

        var locked = await policy.IsLockedAsync("alice", CancellationToken.None);

        Assert.False(locked); // 过期锁定视为未锁定
    }

    [Fact]
    public async Task FailedLogin_AfterLockoutExpiry_StartsCountingAgain()
    {
        var fsql = CreateFreeSql();
        var policy = CreatePolicy(fsql, new AccountOptions { MaxFailedAttempts = 2 });
        await fsql.Insert(new AccountLockoutEntity
        {
            UserName = "alice",
            FailedCount = 1,
            LockoutEnd = DateTime.Now.AddMinutes(-1)
        }).ExecuteAffrowsAsync();

        await policy.OnFailedLoginAsync("alice", CancellationToken.None);

        var rec = fsql.Select<AccountLockoutEntity>().First();
        Assert.Equal(2, rec.FailedCount); // 续计（过期不重置计数，仅解锁判定过期）
    }
}