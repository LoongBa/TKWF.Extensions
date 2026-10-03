using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Account;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// Identity 生产路径集成测试（V4.10.53 领域自治根治，正确路线）——经<b>真实 DI</b>（Initializer ConfigureServices +
/// FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> +
/// <c>User.Use&lt;IUserManager&gt;()</c>（AOP 路径：设 CurrentAopUser → GetRequiredService →
/// AddConstructibleService 守卫工厂 → 经 ActivatorUtilities 直建实现）→ 断言读写落库。
/// <para>对齐 Settings V0.3.0 / Account V0.5.0 测试宿主范式（AccountProductionPathTests）。</para>
/// </summary>
public class IdentityProductionPathTests
{
    private static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        IdentityTestHost.SyncSchema(fsql);
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
        new IdentityExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // Options（门面/接线型 ctor 依赖——消费方 DomainHost 等价注册；IdentityOptions 由消费方绑定 TKWF:Identity 节）
        services.AddOptions<IdentityOptions>();
        services.AddOptions<DomainOptions>();

        // FreeSql 基础设施（消费方 DomainHost 等价注册）
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<UserEntity>, FreeSqlEntityDAC<UserEntity>>();
        services.AddSingleton<IEntityDAC<RoleEntity>, FreeSqlEntityDAC<RoleEntity>>();
        services.AddSingleton<IEntityDAC<UserRoleEntity>, FreeSqlEntityDAC<UserRoleEntity>>();
        // UserRoleView 为 VEntity 只读 DataService——UserRoleViewDataService ctor 依赖 IEntityReadOnlyDAC（FreeSqlEntityDAC 双实现）
        services.AddSingleton<IEntityReadOnlyDAC<UserRoleView>, FreeSqlEntityDAC<UserRoleView>>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Use_IUserManager_ThroughGuardFactory_SaveAndRead()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        // 生产路径：User.Use<IUserManager>()——AOP 路径（设 CurrentAopUser → 守卫工厂 → 直建 UserManager）
        var manager = user.Use<IUserManager>();

        var created = await manager.CreateUserAsync("alice", "secret123", "Alice", CancellationToken.None);
        Assert.NotNull(created);
        Assert.True(created!.Id > 0);

        var loaded = await manager.FindByNameAsync("alice", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("ALICE", loaded!.NormalizedUserName);

        // 落库断言（UserStore → DataService NoAop 直建路径——IEntityDAC 从 DI 解析）
        var fsqlCheck = sp.GetRequiredService<IFreeSql>();
        var saved = await fsqlCheck.Select<UserEntity>().Where(u => u.UserName == "alice").FirstAsync(CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("Alice", saved!.DisplayName);
    }

    [Fact]
    public async Task Use_IUserStore_ThroughGuardFactory_SaveAndRead()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        var store = user.Use<IUserStore>();

        await store.CreateAsync(new UserEntity
        {
            UserName = "bob",
            NormalizedUserName = "BOB",
            DisplayName = "Bob",
            IsActive = true
        }, CancellationToken.None);

        var loaded = await store.GetByUserNameAsync("BOB", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("bob", loaded!.UserName);
    }

    [Fact]
    public async Task Use_IRoleStore_ThroughGuardFactory_SaveAndRead()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        var store = user.Use<IRoleStore>();

        await store.CreateAsync(new RoleEntity { Name = "Admin", DisplayName = "管理员", IsSystemRole = true }, CancellationToken.None);

        var loaded = await store.GetByNameAsync("Admin", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsSystemRole);
    }

    /// <summary>AddConstructibleService 守卫——非 User.Use&lt;T&gt;() 调用链内解析接口必抛（DI004 运行期兜底）。</summary>
    [Fact]
    public void GuardFactory_OutsideUseScope_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IUserManager>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IUserManager", ex.Message);
    }

    /// <summary>实现类 throw-factory——禁止直接 DI 解析（UserStore/RoleStore/UserManager 必须经 User.Use&lt;接口&gt;()）。</summary>
    [Fact]
    public void Implementations_NotDirectlyResolvable()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<UserStore>());
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<RoleStore>());
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<UserManager>());
    }

    /// <summary>
    /// 生产故障修复哨兵：接线型两契约（IAccountPasswordManager / IRoleProvider）经普通 DI 可构造——
    /// 旧 ctor(IDomainUser)（永不注册 DI——D01）致 PermissionChecker/DefaultPasswordResetFlow 普通 DI 构造失败
    /// （真实生产故障）；改 ctor(IServiceProvider) 后全 DI 可解析，GetService 不再抛。
    /// </summary>
    [Fact]
    public void WiringContracts_Resolvable_From_PlainDi()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);

        // Account.DefaultPasswordResetFlow 生产路径：sp.GetService<IAccountPasswordManager>()（C1 普通 DI）
        var passwordManager = sp.GetService<IAccountPasswordManager>();
        Assert.NotNull(passwordManager);
        Assert.IsType<IdentityPasswordManager>(passwordManager);

        // Permissions.PermissionChecker 生产路径：普通 DI 构造注入 IRoleProvider<TUserInfo>
        var roleProvider = sp.GetService<IRoleProvider<TestUserInfo>>();
        Assert.NotNull(roleProvider);
        Assert.IsType<IdentityRoleProvider<TestUserInfo>>(roleProvider);
    }
}
