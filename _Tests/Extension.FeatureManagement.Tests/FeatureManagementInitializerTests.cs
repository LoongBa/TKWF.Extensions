using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interception.Filters;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// FeatureManagementExtensionInitializer 测试——D15 接线。
/// <para>覆盖：[TKWFExtension] 特性声明、ConfigureServices 注册完整
/// （IFeatureManager/IFeatureValueStore/IFeatureChecker/IFeatureDefinitionRepository/IMemoryCache/Options 绑定）、
/// TryAdd 不覆盖消费方实现、贡献者收集（FakeMetaContext 模式，镜像 ConsumerIntegrationTests）、
/// 白名单启用反射断言（ConsumerHostInitializer）、AddFeatureCheck 过滤器注册。</para>
/// </summary>
public class FeatureManagementInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(FeatureManagementExtensionInitializer<FeatureManagementUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("FeatureManagement", attr!.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_IFeatureManager_Descriptor()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFeatureManager));

        Assert.NotNull(descriptor);
        // FeatureManager 构造函数 internal（IFeatureValueStore 等为 internal 契约）→ 工厂注册（对齐 Calendar/FileManagement 先例）
        Assert.NotNull(descriptor!.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IFeatureValueStore_Descriptor()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFeatureValueStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(FeatureValueStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IFeatureChecker_Descriptor()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFeatureChecker));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(FeatureChecker<FeatureManagementUserInfo>), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IFeatureDefinitionRepository_Descriptor()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFeatureDefinitionRepository));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IMemoryCache_Descriptor()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMemoryCache));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(MemoryCache), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_FeatureOptions_Descriptor()
    {
        var services = new ServiceCollection();
        // BindConfiguration 依赖 IConfiguration——空配置（默认值生效）
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<FeatureOptions>>();

        Assert.NotNull(options);
        Assert.Equal(300, options.Value.CacheExpirationSeconds);
    }

    [Fact]
    public void ConfigureServices_CollectsConsumerContributorDefinitions()
    {
        // FakeMetaContext 模式（镜像 Permissions ConsumerIntegrationTests）——静态隔离 try/finally 恢复
        var original = ProjectMetaContextBase.Instance;
        try
        {
            FeatureTestMetaContext.Install();

        var services = new ServiceCollection();
        // BindConfiguration 依赖 IConfiguration——空配置（默认值生效）
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

            var sp = services.BuildServiceProvider();
            var repository = sp.GetRequiredService<IFeatureDefinitionRepository>();

            var names = repository.GetAll().Select(d => d.Name).ToArray();
            Assert.Contains(ConsumerFeatureContributor.BooleanFeature, names);
            Assert.Contains(ConsumerFeatureContributor.StringFeature, names);
            Assert.Contains(ConsumerFeatureContributor.IntFeature, names);
            Assert.Contains(ConsumerFeatureContributor.DecimalFeature, names);
            Assert.Contains(ConsumerFeatureContributor.DateTimeFeature, names);
            Assert.Contains(ConsumerFeatureContributor.JsonFeature, names);
        }
        finally
        {
            FeatureTestMetaContext.Restore(original);
        }
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerManager()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFeatureManager, ConsumerFeatureManager>();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var managerDescriptors = services.Where(d => d.ServiceType == typeof(IFeatureManager)).ToList();
        Assert.Single(managerDescriptors);
        Assert.Equal(typeof(ConsumerFeatureManager), managerDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFeatureValueStore, ConsumerFeatureValueStore>();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var storeDescriptors = services.Where(d => d.ServiceType == typeof(IFeatureValueStore)).ToList();
        Assert.Single(storeDescriptors);
        Assert.Equal(typeof(ConsumerFeatureValueStore), storeDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerChecker()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFeatureChecker, ConsumerFeatureChecker>();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var checkerDescriptors = services.Where(d => d.ServiceType == typeof(IFeatureChecker)).ToList();
        Assert.Single(checkerDescriptors);
        Assert.Equal(typeof(ConsumerFeatureChecker), checkerDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureFilters_AddFeatureCheck_RegistersFeatureFilter()
    {
        var builder = new FilterBuilder<FeatureManagementUserInfo>();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureFilters(builder);

        Assert.Contains(builder.Filters, f => f is FeatureFilterAttribute<FeatureManagementUserInfo>);
    }

    [Fact]
    public void ConsumerHostInitializer_WhitelistsFeatureManagementExtension()
    {
        // 白名单启用（V4.9.85 ADR47）：消费方须显式声明 [TKWFEnabledExtension] 才启用扩展
        var attr = typeof(ConsumerHostInitializer)
            .GetCustomAttributes(typeof(TKWFEnabledExtensionAttribute), false)
            .Cast<TKWFEnabledExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(typeof(FeatureManagementExtensionInitializer<>), attr!.InitializerType);
    }

    // ── 测试专用消费方实现（TryAdd 不覆盖语义） ──

    private sealed class ConsumerFeatureManager : IFeatureManager
    {
        public Task<string> GetValueAsync(string name, IDomainUser? user, string? defaultValue = null, CancellationToken ct = default)
            => Task.FromResult(defaultValue ?? string.Empty);

        public Task<T> GetValueAsync<T>(string name, IDomainUser? user, T defaultValue = default!, CancellationToken ct = default)
            => Task.FromResult(defaultValue);

        public Task<bool> IsEnabledAsync(string name, IDomainUser? user, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<FeatureDefinition>> GetDefinitionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeatureDefinition>>([]);

        public Task<IReadOnlyList<FeatureValueEntity>> GetFeatureValuesAsync(
            string? providerName = null, string? providerKey = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeatureValueEntity>>([]);

        public Task SetValueAsync(string name, string value, string providerName, string? providerKey, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetValueAsync<T>(string name, T value, string providerName, string providerKey, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteValueAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<(string? Value, string ProviderName)> GetEffectiveValueAsync(string name, IDomainUser? user, CancellationToken ct = default)
            => Task.FromResult<(string?, string)>((null, FeatureProviders.Global));
    }

    private sealed class ConsumerFeatureValueStore : IFeatureValueStore
    {
        public Task<FeatureValueEntity?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
            => Task.FromResult<FeatureValueEntity?>(null);

        public Task<IReadOnlyList<FeatureValueEntity>> GetListAsync(string? providerName, string? providerKey, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeatureValueEntity>>(Array.Empty<FeatureValueEntity>());

        public Task SetAsync(FeatureValueEntity entity, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ConsumerFeatureChecker : IFeatureChecker
    {
        public Task<bool> IsEnabledAsync(string featureName, CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
