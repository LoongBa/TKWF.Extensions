using System;
using System.Linq;
using System.Reflection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings.Tests;

/// <summary>
/// SettingsExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、DI 注册、TryAddScoped 语义。
/// </summary>
public class SettingsExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(SettingsExtensionInitializer<SettingsUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Settings", attr.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_ISettingStore_Descriptor()
    {
        var services = new ServiceCollection();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISettingStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(FreeSqlSettingStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_ISettingManager_Descriptor()
    {
        var services = new ServiceCollection();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISettingManager));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(SettingManager), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISettingStore, ConsumerSettingStore>();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        var storeDescriptors = services.Where(d => d.ServiceType == typeof(ISettingStore)).ToList();
        Assert.Single(storeDescriptors);
        Assert.Equal(typeof(ConsumerSettingStore), storeDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerManager()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISettingManager, ConsumerSettingManager>();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        var managerDescriptors = services.Where(d => d.ServiceType == typeof(ISettingManager)).ToList();
        Assert.Single(managerDescriptors);
        Assert.Equal(typeof(ConsumerSettingManager), managerDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_Registers_ScopedLifecycle()
    {
        var services = new ServiceCollection();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        var storeDesc = services.First(d => d.ServiceType == typeof(ISettingStore));
        var managerDesc = services.First(d => d.ServiceType == typeof(ISettingManager));
        Assert.Equal(ServiceLifetime.Scoped, storeDesc.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, managerDesc.Lifetime);
    }

    [Fact]
    public void ConfigureServices_DescriptorCount_IsExactlyTwo()
    {
        var services = new ServiceCollection();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        var storeCount = services.Count(d => d.ServiceType == typeof(ISettingStore));
        var managerCount = services.Count(d => d.ServiceType == typeof(ISettingManager));
        Assert.Equal(1, storeCount);
        Assert.Equal(1, managerCount);
    }

    /// <summary>测试专用 ISettingStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerSettingStore : ISettingStore
    {
        public Task<SettingEntity?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
            => Task.FromResult<SettingEntity?>(null);

        public Task<System.Collections.Generic.IReadOnlyList<SettingEntity>> GetListAsync(string providerName, string? providerKey, CancellationToken ct = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<SettingEntity>>(Array.Empty<SettingEntity>());

        public Task SetAsync(string name, string? value, string providerName, string? providerKey, string? description, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>测试专用 ISettingManager：标记消费方自定义实现。</summary>
    private sealed class ConsumerSettingManager : ISettingManager
    {
        public Task<string> GetAsync(string name, string defaultValue = "", CancellationToken ct = default)
            => Task.FromResult(defaultValue);

        public Task<T> GetAsync<T>(string name, T defaultValue = default!, CancellationToken ct = default)
            => Task.FromResult(defaultValue);

        public Task SetAsync(string name, string value, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetAsync<T>(string name, T value, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
