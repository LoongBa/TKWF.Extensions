using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// V0.2.0（W7 先行）：Permissions 扩展初始化器测试——[TKWFExtension] 特性声明 + ConfigureServices 注册语义。
/// <para>覆盖要点：SG1 发现前提（特性声明）；AddConstructibleService 门面注册形态（接口构造工厂 + 实现类
/// throw-factory + CurrentAopUser 守卫）；消费方自定义 store 优先（TryAdd 不覆盖）；域作用域内解析。</para>
/// <para>V4.10.53（领域自治根治）：IPermissionChecker/IPermissionBatchChecker 注册由 TryAddScoped 改为
/// AddConstructibleService——旧形态构造注入 IDomainUser（永不注册 DI）生产必失败（v0.3.3 同根缺陷）。</para>
/// </summary>
public class PermissionExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(PermissionExtensionInitializer<SimpleUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Permissions", attr.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_DefaultServices()
    {
        var services = new ServiceCollection();
        new PermissionExtensionInitializer<SimpleUserInfo>().ConfigureServices(services);

        var sp = services.BuildServiceProvider();

        // 默认存储：NoOp（消费方未注册 IEntityDAC/自定义 store 时的 fail-closed 回退）
        var store = sp.GetService<IPermissionStore>();
        Assert.NotNull(store);
        Assert.IsType<NoOpPermissionStore>(store);

        // 权限定义仓库：InMemory 收集贡献者定义
        var repository = sp.GetService<IPermissionDefinitionRepository>();
        Assert.NotNull(repository);
        Assert.IsType<InMemoryPermissionDefinitionRepository>(repository);

        // 权限检查器：AddConstructibleService 门面注册——接口为可构造守卫工厂（非实现映射）
        var checkerDesc = services.First(d => d.ServiceType == typeof(IPermissionChecker));
        Assert.Null(checkerDesc.ImplementationType);
        Assert.NotNull(checkerDesc.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, checkerDesc.Lifetime);

        // 实现类注册为 throw-factory——禁止直接 DI 解析
        var implDesc = services.First(d => d.ServiceType == typeof(PermissionChecker<SimpleUserInfo>));
        Assert.NotNull(implDesc.ImplementationFactory);
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // V4.10.53：接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new PermissionExtensionInitializer<SimpleUserInfo>().ConfigureServices(services);
        var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IPermissionChecker>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IPermissionChecker", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_IPermissionBatchChecker_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new PermissionExtensionInitializer<SimpleUserInfo>().ConfigureServices(services);

        // V0.9.0 多用户批量检查器——同实例 AddConstructibleService 注册（接口工厂 + 实现 throw-factory）
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPermissionBatchChecker));
        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        // 消费方先注册自定义 IPermissionStore → TryAddScoped 不应覆盖（store 仍为普通 DI 注册）
        services.AddScoped<IPermissionStore, ConsumerPermissionStore>();
        new PermissionExtensionInitializer<SimpleUserInfo>().ConfigureServices(services);

        var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<IPermissionStore>();
        Assert.IsType<ConsumerPermissionStore>(store);
    }

    [Fact]
    public void ConfigureServices_Registers_ScopedLifecycle()
    {
        var services = new ServiceCollection();
        new PermissionExtensionInitializer<SimpleUserInfo>().ConfigureServices(services);

        var storeDesc = services.First(d => d.ServiceType == typeof(IPermissionStore));
        var checkerDesc = services.First(d => d.ServiceType == typeof(IPermissionChecker));
        Assert.Equal(ServiceLifetime.Scoped, storeDesc.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, checkerDesc.Lifetime);
    }

    /// <summary>测试专用 IPermissionStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerPermissionStore : IPermissionStore
    {
        public Task<PermissionGrantResult> GetAsync(string permissionName, string providerName, string providerKey)
            => Task.FromResult(PermissionGrantResult.Denied);

        public Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted)
            => Task.CompletedTask;

        public Task<HashSet<string>> GetGrantedPermissionNamesAsync(
            string providerName, IEnumerable<string>? providerKeys = null)
            => Task.FromResult(new HashSet<string>(StringComparer.Ordinal));

        public Task<Dictionary<string, HashSet<string>>> GetGrantedPermissionsByProviderKeyAsync(
            string providerName, IEnumerable<string>? providerKeys = null)
            => Task.FromResult(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal));
    }
}
