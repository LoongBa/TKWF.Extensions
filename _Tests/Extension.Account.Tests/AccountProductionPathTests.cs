using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// Account 生产路径集成测试（A5 Store-debt cleanup 后）——经<b>真实 DI</b>（Initializer ConfigureServices +
/// FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> +
/// <c>User.Use&lt;XxxEntityDataService&gt;()</c>（NoAop 路径）→ 断言读写落库。
/// </summary>
public class AccountProductionPathTests
{
    private static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<AccountLockoutEntity>();
        fsql.CodeFirst.SyncStructure<PasswordResetCodeEntity>();
        return fsql;
    }

    private static ServiceProvider CreateProvider(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new AccountExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<AccountLockoutEntity>, FreeSqlEntityDAC<AccountLockoutEntity>>();
        services.AddSingleton<IEntityDAC<PasswordResetCodeEntity>, FreeSqlEntityDAC<PasswordResetCodeEntity>>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Use_LockoutDataService_SaveAndRead_NoAop()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        var dataService = user.Use<AccountLockoutEntityDataService>();

        await dataService.UpsertAsync(new AccountLockoutEntity { UserName = "alice", FailedCount = 2 }, CancellationToken.None);

        var loaded = await dataService.GetByUserNameAsync("alice", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.FailedCount);

        var fsqlCheck = sp.GetRequiredService<IFreeSql>();
        var saved = await fsqlCheck.Select<AccountLockoutEntity>().Where(a => a.UserName == "alice").FirstAsync(CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.FailedCount);
    }

    [Fact]
    public async Task Use_PasswordResetDataService_SaveAndRead_NoAop()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        var dataService = user.Use<PasswordResetCodeEntityDataService>();

        await dataService.SaveAsync(new PasswordResetCodeEntity
        {
            UserName = "alice",
            ResetCode = "ABC12345",
            ExpireTime = DateTime.Now.AddMinutes(30)
        }, CancellationToken.None);

        var loaded = await dataService.GetByUserNameAndCodeAsync("alice", "ABC12345", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.False(loaded!.IsUsed);

        await dataService.MarkUsedAsync(loaded.Id, CancellationToken.None);
        var used = await dataService.GetByUserNameAndCodeAsync("alice", "ABC12345", CancellationToken.None);
        Assert.NotNull(used);
        Assert.True(used!.IsUsed);
    }

    [Fact]
    public void Policy_And_Flow_Resolvable_From_PlainDi()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);

        var policy = sp.GetService<TKW.Framework.Core.AuthController.IAccountLockoutPolicy>();
        var flow = sp.GetService<TKW.Framework.Core.AuthController.IPasswordResetFlow>();

        Assert.NotNull(policy);
        Assert.NotNull(flow);
        Assert.IsType<FreeSqlAccountLockoutPolicy>(policy);
        Assert.IsType<DefaultPasswordResetFlow>(flow);
    }

    [Fact]
    public async Task Policy_FromPlainDi_CanExecuteLockoutCheck()
    {
        using var fsql = CreateInMemoryFreeSql();
        var services = new ServiceCollection();
        services.AddLogging();
        new AccountExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<AccountLockoutEntity>, FreeSqlEntityDAC<AccountLockoutEntity>>();
        services.AddSingleton(AccountTestHost.CreateLockoutDataService(fsql));
        using var sp = services.BuildServiceProvider();

        var policy = sp.GetRequiredService<TKW.Framework.Core.AuthController.IAccountLockoutPolicy>();

        await policy.OnFailedLoginAsync("alice", CancellationToken.None);
        await policy.OnFailedLoginAsync("alice", CancellationToken.None);
        await policy.OnFailedLoginAsync("alice", CancellationToken.None);

        Assert.False(await policy.IsLockedAsync("alice", CancellationToken.None));
        Assert.False(await policy.IsLockedAsync("nonexistent", CancellationToken.None));

        var services2 = new ServiceCollection();
        services2.AddLogging();
        new AccountExtensionInitializer<TestUserInfo>().ConfigureServices(services2);
        services2.Configure<AccountOptions>(o => o.MaxFailedAttempts = 3);
        services2.AddSingleton<IFreeSql>(fsql);
        services2.AddSingleton<UnitOfWorkManager>();
        services2.AddSingleton<IEntityDAC<AccountLockoutEntity>, FreeSqlEntityDAC<AccountLockoutEntity>>();
        services2.AddSingleton(AccountTestHost.CreateLockoutDataService(fsql));
        using var sp2 = services2.BuildServiceProvider();

        var policy2 = sp2.GetRequiredService<TKW.Framework.Core.AuthController.IAccountLockoutPolicy>();
        await policy2.OnFailedLoginAsync("bob", CancellationToken.None);
        await policy2.OnFailedLoginAsync("bob", CancellationToken.None);
        await policy2.OnFailedLoginAsync("bob", CancellationToken.None);

        Assert.True(await policy2.IsLockedAsync("bob", CancellationToken.None));
    }
}
