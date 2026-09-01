using System;
using System.Linq;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// V0.2.0（W7 先行）：Permissions 扩展初始化器测试——[TKWFExtension] 特性声明 + ConfigureServices 注册语义。
/// <para>覆盖要点：SG1 发现前提（特性声明）；TryAddScoped 默认实现注册；
/// 消费方自定义实现优先（TryAdd 不覆盖已注册项——fail-closed 回退契约）。</para>
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

        // 权限检查器：PermissionChecker（fail-closed 默认实现）
        var checker = sp.GetService<IPermissionChecker>();
        Assert.NotNull(checker);
        Assert.IsType<PermissionChecker<SimpleUserInfo>>(checker);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        // 消费方先注册自定义 IPermissionStore → TryAddScoped 不应覆盖
        services.AddScoped<IPermissionStore, ConsumerPermissionStore>();
        new PermissionExtensionInitializer<SimpleUserInfo>().ConfigureServices(services);

        var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<IPermissionStore>();
        Assert.IsType<ConsumerPermissionStore>(store);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerChecker()
    {
        var services = new ServiceCollection();
        // 消费方先注册自定义 IPermissionChecker → TryAddScoped 不应覆盖
        services.AddScoped<IPermissionChecker, ConsumerPermissionChecker>();
        new PermissionExtensionInitializer<SimpleUserInfo>().ConfigureServices(services);

        var sp = services.BuildServiceProvider();

        var checker = sp.GetRequiredService<IPermissionChecker>();
        Assert.IsType<ConsumerPermissionChecker>(checker);
    }

    [Fact]
    public void ConfigureServices_Registers_ScopedLifecycle()
    {
        var services = new ServiceCollection();
        new PermissionExtensionInitializer<SimpleUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(IPermissionStore));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>测试专用 IPermissionStore：标记消费方自定义实现。 </summary>
    private sealed class ConsumerPermissionStore : IPermissionStore
    {
        public Task<PermissionGrantResult> GetAsync(string permissionName, string providerName, string providerKey)
            => Task.FromResult(PermissionGrantResult.Denied);

        public Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted)
            => Task.CompletedTask;
    }

    /// <summary>测试专用 IPermissionChecker：标记消费方自定义实现。 </summary>
    private sealed class ConsumerPermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(true);

        public Task<Dictionary<string, bool>> IsGrantedAsync(params string[] permissionNames)
            => Task.FromResult(permissionNames.ToDictionary(n => n, _ => true));
    }
}