using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// Account 生产路径集成测试（V4.10.53 领域自治根治，正确路线）——经<b>真实 DI</b>（Initializer ConfigureServices +
/// FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> +
/// <c>User.Use&lt;IAccountLockoutStore&gt;()</c>（AOP 路径：设 CurrentAopUser → GetRequiredService →
/// AddConstructibleService 守卫工厂 → 经 ActivatorUtilities 直建实现）→ 断言读写落库。
/// <para>对齐 Settings V0.3.0 测试宿主范式（SettingManagerTests.CreateProductionHost）。</para>
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

    /// <summary>
    /// 构建消费方生产形态 DI：Initializer ConfigureServices + FreeSql 基础设施。
    /// DataService 不手动注册——生产经 SG 消费方聚合自动注册（throw-factory）；
    /// 测试经 <c>Use&lt;具体类&gt;()</c> NoAop 路径直建（IEntityDAC 从 DI 解析，红线合规）。
    /// </summary>
    private static ServiceProvider CreateProvider(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new AccountExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // FreeSql 基础设施（消费方 DomainHost 等价注册）
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<AccountLockoutEntity>, FreeSqlEntityDAC<AccountLockoutEntity>>();
        services.AddSingleton<IEntityDAC<PasswordResetCodeEntity>, FreeSqlEntityDAC<PasswordResetCodeEntity>>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Use_IAccountLockoutStore_SaveAndRead_ThroughGuardFactory()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        // 生产路径：User.Use<IAccountLockoutStore>()——AOP 路径（设 CurrentAopUser → 守卫工厂 → 直建实现）
        var store = user.Use<IAccountLockoutStore>();

        await store.SaveAsync(new AccountLockoutEntity { UserName = "alice", FailedCount = 2 }, CancellationToken.None);

        var loaded = await store.GetAsync("alice", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.FailedCount);

        // 落库断言（DataService NoAop 直建路径——IEntityDAC 从 DI 解析）
        var fsqlCheck = sp.GetRequiredService<IFreeSql>();
        var saved = await fsqlCheck.Select<AccountLockoutEntity>().Where(a => a.UserName == "alice").FirstAsync(CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.FailedCount);
    }

    [Fact]
    public async Task Use_IPasswordResetStore_SaveAndRead_ThroughGuardFactory()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        var store = user.Use<IPasswordResetStore>();

        await store.SaveAsync(new PasswordResetCodeEntity
        {
            UserName = "alice",
            ResetCode = "ABC12345",
            ExpireTime = DateTime.Now.AddMinutes(30)
        }, CancellationToken.None);

        var loaded = await store.GetAsync("alice", "ABC12345", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.False(loaded!.IsUsed);

        await store.MarkUsedAsync(loaded.Id, CancellationToken.None);
        var used = await store.GetAsync("alice", "ABC12345", CancellationToken.None);
        Assert.NotNull(used);
        Assert.True(used!.IsUsed);
    }

    /// <summary>生产故障修复哨兵：Policy/Flow 经普通 DI 可构造（旧 ctor 注入 IDomainUser 永不注册 → 构造失败、锁定检查静默失效）。</summary>
    [Fact]
    public void Policy_And_Flow_Resolvable_From_PlainDi()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);

        // AuthController 生产路径：User.GetOptionalService<IAccountLockoutPolicy>() → sp.GetService（接线型普通 DI）
        var policy = sp.GetService<TKW.Framework.Core.AuthController.IAccountLockoutPolicy>();
        var flow = sp.GetService<TKW.Framework.Core.AuthController.IPasswordResetFlow>();

        Assert.NotNull(policy);   // V4.10.53 修复点：接线型 ctor(IServiceProvider,IOptions,ILogger) 全可解析
        Assert.NotNull(flow);
        Assert.IsType<FreeSqlAccountLockoutPolicy>(policy);
        Assert.IsType<DefaultPasswordResetFlow>(flow);
    }

    /// <summary>接线型 Policy 经真实 DI 解析后可执行锁定检查（Store 直构注册——Store 守卫路径已由
    /// <c>Use_IAccountLockoutStore</c> 覆盖，此处验证 Policy→Store 组合生产形态）。</summary>
    [Fact]
    public async Task Policy_FromPlainDi_CanExecuteLockoutCheck()
    {
        using var fsql = CreateInMemoryFreeSql();
        var services = new ServiceCollection();
        services.AddLogging();
        new AccountExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddSingleton<IAccountLockoutStore>(AccountTestHost.CreateLockoutStore(fsql));
        using var sp = services.BuildServiceProvider();

        var policy = sp.GetRequiredService<TKW.Framework.Core.AuthController.IAccountLockoutPolicy>();

        await policy.OnFailedLoginAsync("alice", CancellationToken.None);
        await policy.OnFailedLoginAsync("alice", CancellationToken.None);
        await policy.OnFailedLoginAsync("alice", CancellationToken.None);

        // 默認閾值 5——3 次未达 → 未锁定
        Assert.False(await policy.IsLockedAsync("alice", CancellationToken.None));
        Assert.True(await policy.IsLockedAsync("nonexistent", CancellationToken.None) == false);

        // Options 覆盖验证：降低阈值（Configure 后重新解析——新作用域）
        var services2 = new ServiceCollection();
        services2.AddLogging();
        new AccountExtensionInitializer<TestUserInfo>().ConfigureServices(services2);
        services2.Configure<AccountOptions>(o => o.MaxFailedAttempts = 3);
        services2.AddSingleton<IAccountLockoutStore>(AccountTestHost.CreateLockoutStore(fsql));
        using var sp2 = services2.BuildServiceProvider();

        var policy2 = sp2.GetRequiredService<TKW.Framework.Core.AuthController.IAccountLockoutPolicy>();
        await policy2.OnFailedLoginAsync("bob", CancellationToken.None);
        await policy2.OnFailedLoginAsync("bob", CancellationToken.None);
        await policy2.OnFailedLoginAsync("bob", CancellationToken.None);

        Assert.True(await policy2.IsLockedAsync("bob", CancellationToken.None));
    }
}