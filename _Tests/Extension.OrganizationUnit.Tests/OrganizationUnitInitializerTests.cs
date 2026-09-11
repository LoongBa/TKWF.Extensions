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

    [Fact]
    public void ConfigureServices_Registers_Manager_Descriptor()
    {
        var services = new ServiceCollection();
        new OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOrganizationUnitManager));

        Assert.NotNull(descriptor);
        // OrganizationUnitManager 构造函数 internal（IOrganizationUnitStore 为 internal 契约）→ 工厂注册；
        // TryAddScoped 工厂语义与类型注册等价（消费方自定义实现仍优先）
        Assert.NotNull(descriptor!.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

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
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerManager()
    {
        var services = new ServiceCollection();
        services.AddScoped<IOrganizationUnitManager, ConsumerOrganizationUnitManager>();
        new OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IOrganizationUnitManager)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerOrganizationUnitManager), descriptors[0].ImplementationType);
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

    /// <summary>测试专用 IOrganizationUnitManager：标记消费方自定义实现（仅 DI 标记，不实际调用）。</summary>
    private sealed class ConsumerOrganizationUnitManager : IOrganizationUnitManager
    {
        public Task<OrganizationUnitEntity> CreateAsync(string code, string name, long? parentId, int? sortOrder = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(long id, string? name = null, int? sortOrder = null, bool? isEnabled = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task MoveAsync(long id, long? newParentId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<OrganizationUnitTreeNode> GetTreeAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrganizationUnitEntity>> GetSubTreeAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrganizationUnitEntity>> GetAncestorsAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AssignUserAsync(long ouId, string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UnassignUserAsync(long ouId, string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetUserIdsInOrganizationUnitAsync(long ouId, bool includeDescendants, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<long>> GetOrganizationUnitIdsForUserAsync(string userId, CancellationToken ct = default) => throw new NotImplementedException();
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
        public Task AddUserAsync(OrganizationUnitUserEntity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveUserAsync(long organizationUnitId, string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetUserIdsByOrganizationUnitIdsAsync(IReadOnlyList<long> organizationUnitIds, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<long>> GetOrganizationUnitIdsForUserAsync(string userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CountUsersByOrganizationUnitIdAsync(long organizationUnitId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteUsersByOrganizationUnitIdsAsync(IReadOnlyList<long> organizationUnitIds, CancellationToken ct = default) => throw new NotImplementedException();
    }
}