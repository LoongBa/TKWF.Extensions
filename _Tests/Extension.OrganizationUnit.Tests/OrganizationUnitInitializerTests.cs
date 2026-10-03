using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKWF.Ext.OrganizationUnit;

namespace TKWF.Ext.OrganizationUnit.Tests;

/// <summary>
/// D14：OrganizationUnitExtensionInitializer 测试——[TKWFExtension] 特性声明、DI 注册完整、TryAddScoped 不覆盖消费方、白名单声明。
/// <para>V4.10.53（领域自治根治，ADR90，正确路线）注册形态二态：</para>
/// <list type="bullet">
/// <item><see cref="IOrganizationUnitManager"/>（接口 : IDomainService）→ <b>AddConstructibleService</b>：接口 = 可构造守卫工厂
///     （非实现映射）+ 实现类 = throw-factory；域作用域外直接 DI 解析接口必抛领域架构守卫。</item>
/// <item><see cref="IOrganizationUnitStore"/>（internal 接线型契约）→ <b>TryAddScoped 普通 DI</b>（ImplementationType + 消费方自定义优先）。</item>
/// </list>
/// </summary>
public class OrganizationUnitInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("OrganizationUnit", attr!.Name);
    }

    // ── IOrganizationUnitManager：AddConstructibleService（标准门面）──

    [Fact]
    public void ConfigureServices_Registers_Manager_GuardFactory()
    {
        var services = new ServiceCollection();
        new OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口注册为可构造守卫工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOrganizationUnitManager));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_Manager_ThrowFactory()
    {
        var services = new ServiceCollection();
        new OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<IOrganizationUnitManager>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(OrganizationUnitManager));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<OrganizationUnitManager>());
    }

    [Fact]
    public void ManagerInterface_OutsideUseScope_Throws()
    {
        // AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOrganizationUnitManager>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IOrganizationUnitManager", ex.Message);
    }

    // ── Store：内部接线型 TryAddScoped（ImplementationType 普通 DI）──

    [Fact]
    public void ConfigureServices_Registers_Store_Descriptor()
    {
        var services = new ServiceCollection();
        new OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOrganizationUnitStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(OrganizationUnitStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_BothDataServices()
    {
        // v4.10.8 (ADR61)：Initializer 不再手动注册 DataService——SG 基类类型判定生成，
        // 经扩展 ProjectMetaContext.GetServiceRegistrations() 暴露（消费方聚合自动注册为可构造工厂）。
        var regs = TKWF.Ext.OrganizationUnit.Generated.ProjectMetaContext.GetOrCreateInstance()
            .GetServiceRegistrations().ToList();

        Assert.Contains(regs, r => r.Implementation == typeof(OrganizationUnitEntityDataService));
        Assert.Contains(regs, r => r.Implementation == typeof(OrganizationUnitUserEntityDataService));
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<IOrganizationUnitStore, ConsumerOrganizationUnitStore>();
        new OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IOrganizationUnitStore)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerOrganizationUnitStore), descriptors[0].ImplementationType);
    }

    [Fact]
    public void ConsumerHostInitializer_Declares_EnabledExtensionWhitelist()
    {
        var attr = typeof(ConsumerHostInitializer)
            .GetCustomAttributes(typeof(TKWFEnabledExtensionAttribute), false)
            .Cast<TKWFEnabledExtensionAttribute>()
            .FirstOrDefault();

        // V4.9.85 (ADR47)：消费方白名单声明——发现不自动启用，须显式声明三钩子才接线
        Assert.NotNull(attr);
        Assert.Equal(typeof(OrganizationUnitExtensionInitializer<>), attr!.InitializerType);
    }

    /// <summary>测试专用 IOrganizationUnitStore：标记消费方自定义实现（仅 DI 标记，不实际调用）。</summary>
    private sealed class ConsumerOrganizationUnitStore : IOrganizationUnitStore
    {
        public Task<long> CreateAsync(OrganizationUnitEntity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(OrganizationUnitEntity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<OrganizationUnitEntity?> GetByIdAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<OrganizationUnitEntity?> GetByCodeAsync(string code, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrganizationUnitEntity>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrganizationUnitEntity>> GetByPathPrefixAsync(string pathPrefix, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrganizationUnitEntity>> GetByCodesAsync(IReadOnlyList<string> codes, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetUserIdsByOuPathPrefixAsync(string ouPathPrefix, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddUserAsync(OrganizationUnitUserEntity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveUserAsync(long organizationUnitId, string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetUserIdsByOrganizationUnitIdsAsync(IReadOnlyList<long> organizationUnitIds, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<long>> GetOrganizationUnitIdsForUserAsync(string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CountUsersByOrganizationUnitIdAsync(long organizationUnitId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteUsersByOrganizationUnitIdsAsync(IReadOnlyList<long> organizationUnitIds, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
