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

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// IdentityExtensionInitializer 测试——[TKWFExtension] 特性声明、DI 注册、TryAddScoped 语义、种子初始化。
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

    [Fact]
    public void ConfigureServices_Registers_IUserStore_Descriptor()
    {
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IUserStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(UserStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IRoleStore_Descriptor()
    {
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRoleStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(RoleStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IUserManager_Descriptor()
    {
        var services = new ServiceCollection();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IUserManager));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(UserManager), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerUserStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserStore, ConsumerUserStore>();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IUserStore)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerUserStore), descriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerRoleStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRoleStore, ConsumerRoleStore>();
        new IdentityExtensionInitializer<IdentityUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IRoleStore)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerRoleStore), descriptors[0].ImplementationType);
    }

    [Fact]
    public async Task InitializeAsync_SeedsAdminRole_Idempotent()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<RoleEntity>();

        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddLogging();
        // 数据访问红线整改：RoleStore 委托 DataService——注册完整 DI 链（UoW/DAC/DataService/User）
        services.AddScoped<UnitOfWorkManager>();
        services.AddScoped<IEntityDAC<RoleEntity>>(sp => new FreeSqlEntityDAC<RoleEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<UserRoleEntity>>(sp => new FreeSqlEntityDAC<UserRoleEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        // v4.10.8 (ADR61) 迁移：DataService 注册改为测试版可构造工厂（镜像生产 AddConstructibleDataService）
        AddTestConstructibleDataService<RoleEntityDataService>(services);
        AddTestConstructibleDataService<UserRoleEntityDataService>(services);
        services.AddScoped<IDomainUser>(_ => new StubDomainUser());
        services.TryAddScoped<IRoleStore, RoleStore>();
        var sp = services.BuildServiceProvider();

        var init = new IdentityExtensionInitializer<IdentityUserInfo> { ServiceProvider = sp };
        await init.InitializeAsync();
        await init.InitializeAsync(); // 第二次调用应幂等

        var roleStore = sp.GetRequiredService<IRoleStore>();
        var roleList = await roleStore.GetListAsync(take: 100, ct: CancellationToken.None);
        Assert.Single(roleList);
        var admin = await roleStore.GetByNameAsync("Admin", CancellationToken.None);
        Assert.NotNull(admin);
        Assert.True(admin!.IsSystemRole);
    }

    [Fact]
    public async Task InitializeAsync_NoServiceProvider_NoOp()
    {
        var init = new IdentityExtensionInitializer<IdentityUserInfo>(); // ServiceProvider = null

        // 不应抛异常
        await init.InitializeAsync();
    }

    /// <summary>v4.10.8 (ADR61) 迁移：测试版可构造 DataService 工厂——镜像生产
    /// <c>AddConstructibleDataService</c>（<c>ActivatorUtilities.CreateInstance</c> + 域用户），
    /// 用户源改为 DI <c>IDomainUser</c>（StubDomainUser）而非 AsyncLocal <c>CurrentAopUser</c>——
    /// 免域作用域、xUnit 并行隔离安全（不设 AsyncLocal）。</summary>
    private static void AddTestConstructibleDataService<T>(IServiceCollection services)
        where T : class
    {
        services.AddScoped<T>(sp =>
        {
            var user = sp.GetRequiredService<IDomainUser>();
            return (T)ActivatorUtilities.CreateInstance(sp, typeof(T), user);
        });
    }

    /// <summary>测试专用 IUserStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerUserStore : IUserStore
    {
        public Task<UserEntity?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<UserEntity?>(null);
        public Task<UserEntity?> GetByUserNameAsync(string normalizedUserName, CancellationToken ct = default) => Task.FromResult<UserEntity?>(null);
        public Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<UserEntity?>(null);
        public Task<IReadOnlyList<UserEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserEntity>>(Array.Empty<UserEntity>());
        public Task CreateAsync(UserEntity user, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(UserEntity user, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(long id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<RoleEntity>> GetRolesAsync(long userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RoleEntity>>(Array.Empty<RoleEntity>());
        public Task AssignRoleAsync(long userId, long roleId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveRoleAsync(long userId, long roleId, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>测试专用 IRoleStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerRoleStore : IRoleStore
    {
        public Task<RoleEntity?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<RoleEntity?>(null);
        public Task<RoleEntity?> GetByNameAsync(string name, CancellationToken ct = default) => Task.FromResult<RoleEntity?>(null);
        public Task<IReadOnlyList<RoleEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RoleEntity>>(Array.Empty<RoleEntity>());
        public Task CreateAsync(RoleEntity role, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RoleEntity role, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(long id, CancellationToken ct = default) => Task.FromResult(false);
    }
}