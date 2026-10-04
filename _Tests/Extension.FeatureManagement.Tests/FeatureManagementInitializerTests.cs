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
/// （IFeatureManager/IFeatureValueStore = AddConstructibleService 守卫工厂 + throw-factory + 域外抛；
/// IFeatureChecker = TryAddScoped ImplementationType 接线型；4 Provider = TryAddEnumerable ×4；
/// IFeatureDefinitionRepository/IMemoryCache/Options 绑定）、贡献者收集（FakeMetaContext 模式，镜像 ConsumerIntegrationTests）、
/// 白名单启用反射断言（ConsumerHostInitializer）、AddFeatureCheck 过滤器注册。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：Store/Manager 注册由 TryAddScoped 改 <c>AddConstructibleService</c>——
/// 消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析（AOP 路径先设 CurrentAopUser 再 GetRequiredService）。</para>
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

    // ── V4.10.53：IFeatureManager = AddConstructibleService（守卫工厂 + throw-factory + 域外抛） ──

    [Fact]
    public void ConfigureServices_Registers_IFeatureManager_GuardFactoryDescriptor()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        // AddConstructibleService——接口注册为可构造守卫工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFeatureManager));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_FeatureManager_ThrowFactory()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<IFeatureManager>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(FeatureManager));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<FeatureManager>());
    }

    [Fact]
    public void InterfaceFactory_IFeatureManager_OutsideUseScope_Throws()
    {
        // 接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IFeatureManager>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IFeatureManager", ex.Message);
    }

    // ── V4.10.53：IFeatureValueStore = AddConstructibleService（守卫工厂 + throw-factory + 域外抛） ──

    [Fact]
    public void ConfigureServices_Registers_IFeatureValueStore_GuardFactoryDescriptor()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFeatureValueStore));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_FeatureValueStore_ThrowFactory()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(FeatureValueStore));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<FeatureValueStore>());
    }

    [Fact]
    public void InterfaceFactory_IFeatureValueStore_OutsideUseScope_Throws()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IFeatureValueStore>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IFeatureValueStore", ex.Message);
    }

    // ── IFeatureChecker = TryAddScoped ImplementationType（接线型普通 DI——框架过滤器 GetService 解析） ──

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

    // ── 4 Provider = TryAddEnumerableConstructible ×4（V4.10.55 ADR92——集合版守卫工厂，工厂委托形态） ──

    [Fact]
    public void ConfigureServices_Registers_FourValueProviders_TryAddEnumerableConstructible()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        var providers = services.Where(d => d.ServiceType == typeof(IFeatureValueProvider)).ToList();

        Assert.Equal(4, providers.Count);
        // TryAddEnumerableConstructible：工厂委托形态（ImplementationType=null + ImplementationFactory 非空）——
        // 帧内（FeatureManager 经 User.Use<IFeatureManager>() 创建时）经 CurrentAopUser 供给构造
        Assert.All(providers, d => Assert.Null(d.ImplementationType));
        Assert.All(providers, d => Assert.NotNull(d.ImplementationFactory));
        Assert.All(providers, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
        // 集合版守卫工厂自定义去重（Oracle P0-1）——4 个不同实现各自独立工厂委托
        Assert.Equal(4, providers.Select(d => d.ImplementationFactory).Distinct().Count());
    }

    /// <summary>实现类 throw-factory（禁直接 DI 直取——集合元素须经 User.Use 帧内枚举创建）。</summary>
    [Fact]
    public void ConfigureServices_ProviderImpl_ThrowFactory()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);

        foreach (var implType in new[]
                 {
                     typeof(UserFeatureValueProvider), typeof(RoleFeatureValueProvider),
                     typeof(TenantFeatureValueProvider), typeof(GlobalFeatureValueProvider)
                 })
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == implType);
            Assert.NotNull(descriptor);
            Assert.NotNull(descriptor!.ImplementationFactory);

            using var provider = services.BuildServiceProvider();
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService(implType));
        }
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

    // ── V4.10.53：消费方覆盖语义（AddConstructibleService = AddScoped——后注册覆盖默认；Checker TryAdd 保留） ──

    [Fact]
    public void ConsumerManager_RegisteredAfterInitializer_OverridesGuardFactory()
    {
        // AddConstructibleService 用 AddScoped（非 TryAdd）——消费方 OnRegisterDomainServices 后注册即覆盖默认（对齐 Identity README 语义）
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);
        services.AddScoped<IFeatureManager, ConsumerFeatureManager>();

        var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IFeatureManager>();

        Assert.IsType<ConsumerFeatureManager>(manager);   // 后注册覆盖守卫工厂
    }

    [Fact]
    public void ConsumerStore_RegisteredAfterInitializer_OverridesGuardFactory()
    {
        var services = new ServiceCollection();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);
        services.AddScoped<IFeatureValueStore, ConsumerFeatureValueStore>();

        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IFeatureValueStore>();

        Assert.IsType<ConsumerFeatureValueStore>(store);   // 后注册覆盖守卫工厂
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

    // ── 测试专用消费方实现（后注册覆盖 / TryAdd 不覆盖语义） ──

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
