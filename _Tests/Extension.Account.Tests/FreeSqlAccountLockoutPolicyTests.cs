using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.AuthController;
using TKW.Framework.Domain;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// FreeSqlAccountLockoutPolicy 测试——锁定判定/失败递增到阈值/成功重置/解锁/过期自动解锁。
/// <para>V4.10.53（领域自治根治，正确路线）：Policy 改<b>接线型</b>（skill §4.2）——ctor
/// <c>(IServiceProvider, IOptions&lt;AccountOptions&gt;, ILogger)</c>，不注入 IDomainUser（永不注册 DI，生产
/// AuthController 普通 DI 构造失败修复）；Store 经 <see cref="IServiceProvider"/> 延迟解析（C1 模式）。
/// 测试经<b>真实 DI</b>（Initializer ConfigureServices + AddLogging）构建 Policy；Store 直构真实实例后注册
/// （守卫工厂路径由 Initializer 测试 + 生产路径集成测试覆盖，此处聚焦 Policy 业务断言语义）。</para>
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
        var services = new ServiceCollection();
        services.AddLogging();
        new AccountExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        // options 覆盖（默认值兜底：不传则用 AccountOptions 默认）
        services.Configure<AccountOptions>(o =>
        {
            if (options == null) return;
            o.MaxFailedAttempts = options.MaxFailedAttempts;
            o.DefaultLockoutMinutes = options.DefaultLockoutMinutes;
            o.ResetCodeValidityMinutes = options.ResetCodeValidityMinutes;
            o.IsEnabled = options.IsEnabled;
        });
        // Store 直构真实实例注册（Policy 经 IServiceProvider 解析；守卫路径由其他测试覆盖）
        services.AddSingleton<IAccountLockoutStore>(AccountTestHost.CreateLockoutStore(fsql));
        var sp = services.BuildServiceProvider();
        return (FreeSqlAccountLockoutPolicy)sp.GetRequiredService<IAccountLockoutPolicy>();
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
        var rec = await AccountTestHost.CreateLockoutStore(fsql).GetAsync("alice", CancellationToken.None);
        Assert.NotNull(rec);
        Assert.Equal(2, rec!.FailedCount);
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
        var rec = await AccountTestHost.CreateLockoutStore(fsql).GetAsync("alice", CancellationToken.None);
        Assert.NotNull(rec);
        Assert.NotNull(rec!.LockoutEnd);
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
        Assert.Null(await AccountTestHost.CreateLockoutStore(fsql).GetAsync("alice", CancellationToken.None));
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
        Assert.Null(await AccountTestHost.CreateLockoutStore(fsql).GetAsync("alice", CancellationToken.None));
    }

    [Fact]
    public async Task IsLocked_ExpiredLockout_ReturnsFalse()
    {
        var fsql = CreateFreeSql();
        var policy = CreatePolicy(fsql);
        await AccountTestHost.CreateLockoutStore(fsql).SaveAsync(new AccountLockoutEntity {
            UserName = "alice",
            FailedCount = 1,
            LockoutEnd = DateTime.Now.AddMinutes(-1) // 已过期
        }, CancellationToken.None);

        var locked = await policy.IsLockedAsync("alice", CancellationToken.None);

        Assert.False(locked); // 过期锁定视为未锁定
    }

    [Fact]
    public async Task FailedLogin_AfterLockoutExpiry_StartsCountingAgain()
    {
        var fsql = CreateFreeSql();
        var policy = CreatePolicy(fsql, new AccountOptions { MaxFailedAttempts = 2 });
        await AccountTestHost.CreateLockoutStore(fsql).SaveAsync(new AccountLockoutEntity {
            UserName = "alice",
            FailedCount = 1,
            LockoutEnd = DateTime.Now.AddMinutes(-1)
        }, CancellationToken.None);

        await policy.OnFailedLoginAsync("alice", CancellationToken.None);

        var rec = await AccountTestHost.CreateLockoutStore(fsql).GetAsync("alice", CancellationToken.None);
        Assert.NotNull(rec);
        Assert.Equal(2, rec!.FailedCount); // 续计（过期不重置计数，仅解锁判定过期）
    }
}