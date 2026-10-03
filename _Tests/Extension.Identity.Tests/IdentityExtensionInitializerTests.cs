using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Account;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// IdentityExtensionInitializer 测试——[TKWFExtension] 特性声明、DI 注册形态、种子初始化。
/// <para>V4.10.53（领域自治根治，正确路线）注册形态：</para>
/// <list type="bullet">
/// <item><see cref="IUserStore"/> / <see cref="IRoleStore"/> / <see cref="IUserManager"/>（接口 : IDomainService）→
///     <c>AddConstructibleService</c>：接口 = 可构造守卫工厂（非实现映射）+ 实现类 = throw-factory；
///     域作用域外（无 CurrentAopUser）直接 DI 解析接口必抛领域架构守卫；</item>
/// <item><see cref="IAccountPasswordManager"/>（Account.Abstractions 契约，非 IDomainService）→ TryAddScoped 接线型普通 DI；
///     <see cref="IRoleProvider{TUserInfo}"/>（Permissions.Abstractions 契约，非 IDomainService）→ AddScoped 接线型普通 DI。</item>
/// </list>
/// <para>种子：核心提取为 <see cref="IdentityExtensionInitializer{TUserInfo}.SeedAdminRoleAsync"/> 供直测（免
/// DomainHost/System 作用域）；<see cref="IdentityExtensionInitializer{TUserInfo}.InitializeAsync"/> 的守卫跳过路径
/// （无 IEntityDAC 真实持久化）不经宿主，仍直测 InitializeAsync——System 作用域编排为 Permissions/Tagging 同款已证
/// 模式（方案 A'），其完整宿主路径不经测试宿主（对齐 Permissions Seed 测试先例）。</para>
/// </summary>
public class IdentityExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(IdentityExtensionInitializer<IdentityUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Identity", attr.Name);
    }

    // ── AddConstructibleService 三接口：守卫工厂形态 ──

    [Fact]
    public void ConfigureServices_Registers_IUserStore_GuardFactory()
    {
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IUserStore));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);   // AddConstructibleService——非实现映射
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IRoleStore_GuardFactory()
    {
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRoleStore));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IUserManager_GuardFactory()
    {
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IUserManager));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_Implementations_ThrowFactory()
    {
        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<接口>() 创建
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var userStoreImpl = services.FirstOrDefault(d => d.ServiceType == typeof(UserStore));
        var roleStoreImpl = services.FirstOrDefault(d => d.ServiceType == typeof(RoleStore));
        var userManagerImpl = services.FirstOrDefault(d => d.ServiceType == typeof(UserManager));
        Assert.NotNull(userStoreImpl);
        Assert.NotNull(userStoreImpl!.ImplementationFactory);
        Assert.NotNull(roleStoreImpl);
        Assert.NotNull(roleStoreImpl!.ImplementationFactory);
        Assert.NotNull(userManagerImpl);
        Assert.NotNull(userManagerImpl!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<UserStore>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<RoleStore>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<UserManager>());
    }

    [Fact]
    public void StoreInterfaceFactories_OutsideUseScope_Throw()
    {
        // AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex1 = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IUserStore>());
        Assert.Contains("领域架构守卫", ex1.Message);
        Assert.Contains("IUserStore", ex1.Message);

        var ex2 = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IRoleStore>());
        Assert.Contains("领域架构守卫", ex2.Message);
        Assert.Contains("IRoleStore", ex2.Message);

        var ex3 = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IUserManager>());
        Assert.Contains("领域架构守卫", ex3.Message);
        Assert.Contains("IUserManager", ex3.Message);
    }

    // ── 接线型两契约：普通 DI 可解析（ImplementationType 存在）──

    [Fact]
    public void ConfigureServices_Registers_IAccountPasswordManager_Descriptor()
    {
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAccountPasswordManager));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(IdentityPasswordManager), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IRoleProvider_Descriptor()
    {
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRoleProvider<IdentityUserInfo>));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(IdentityRoleProvider<IdentityUserInfo>), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    // ── 种子：核心直测 + InitializeAsync 守卫跳过路径 ──

    [Fact]
    public async Task SeedAdminRole_SeedsAdminRole_Idempotent()
    {
        var fsql = CreateFreeSql();
        fsql.CodeFirst.SyncStructure<RoleEntity>();
        var store = IdentityTestHost.CreateRoleStore(fsql);

        await IdentityExtensionInitializer<IdentityUserInfo>.SeedAdminRoleAsync(store);
        await IdentityExtensionInitializer<IdentityUserInfo>.SeedAdminRoleAsync(store); // 幂等

        var roleList = await store.GetListAsync(take: 100, ct: CancellationToken.None);
        Assert.Single(roleList);
        var admin = await store.GetByNameAsync("Admin", CancellationToken.None);
        Assert.NotNull(admin);
        Assert.True(admin!.IsSystemRole);
    }

    [Fact]
    public async Task SeedAdminRole_DoesNotOverwriteExistingRole()
    {
        var fsql = CreateFreeSql();
        fsql.CodeFirst.SyncStructure<RoleEntity>();
        var store = IdentityTestHost.CreateRoleStore(fsql);
        // 消费方已存在 Admin 角色（非系统角色）
        await store.CreateAsync(new RoleEntity { Name = "Admin", DisplayName = "既有管理员", IsSystemRole = false }, CancellationToken.None);

        await IdentityExtensionInitializer<IdentityUserInfo>.SeedAdminRoleAsync(store);

        var admin = await store.GetByNameAsync("Admin", CancellationToken.None);
        Assert.NotNull(admin);
        Assert.False(admin!.IsSystemRole);   // 未被种子覆盖为系统角色
        Assert.Equal("既有管理员", admin.DisplayName);
    }

    [Fact]
    public async Task InitializeAsync_NoDacRegistered_SkipsSeeding()
    {
        // 未注册 IEntityDAC<RoleEntity>（真实持久化未接线）→ 种子守卫返回（不经 DomainHost/System 作用域——无宿主也安全）
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var init = new IdentityExtensionInitializer<IdentityUserInfo>();

        await init.InitializeAsync(sp); // 不应抛异常
    }

    [Fact]
    public async Task InitializeAsync_NullServiceProvider_Throws()
    {
        // V4.10.25 (ADR78)：签名强制带 IServiceProvider——null 属误用（DomainHost 保证非 null），抛异常而非静默
        var init = new IdentityExtensionInitializer<IdentityUserInfo>();
        await Assert.ThrowsAsync<ArgumentNullException>(() => init.InitializeAsync(null!));
    }

    private static IFreeSql CreateFreeSql()
        => new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
}
