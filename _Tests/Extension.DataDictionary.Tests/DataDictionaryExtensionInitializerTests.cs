using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// DataDictionaryExtensionInitializer 测试——[TKWFExtension] 特性声明、DI 注册、TryAddScoped 语义。
/// </summary>
public class DataDictionaryExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(DataDictionaryExtensionInitializer<DataDictionaryUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("DataDictionary", attr.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_IDictionaryStore_Descriptor()
    {
        var services = new ServiceCollection();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDictionaryStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(FreeSqlDictionaryStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IDictionaryManager_Descriptor()
    {
        var services = new ServiceCollection();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDictionaryManager));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(DictionaryManager), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDictionaryStore, ConsumerDictionaryStore>();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IDictionaryStore)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerDictionaryStore), descriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerManager()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDictionaryManager, ConsumerDictionaryManager>();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IDictionaryManager)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerDictionaryManager), descriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_Registers_IMemoryCache()
    {
        var services = new ServiceCollection();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMemoryCache));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_DataDictionaryOptions()
    {
        var services = new ServiceCollection();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<Microsoft.Extensions.Options.IOptions<DataDictionaryOptions>>();

        // AddOptions<DataDictionaryOptions>() 为内部注册——通过容器解析 IOptions<T> 验证默认值生效
        Assert.NotNull(options);
        Assert.True(options!.Value.EnableCache);
        Assert.Equal(300, options.Value.CacheExpirationSeconds);
    }

    /// <summary>测试专用 IDictionaryStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerDictionaryStore : IDictionaryStore
    {
        public Task<DictionaryDefinitionEntity?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default) => Task.FromResult<DictionaryDefinitionEntity?>(null);
        public Task<DictionaryDefinitionEntity?> GetDefinitionByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<DictionaryDefinitionEntity?>(null);
        public Task<DictionaryItemEntity?> GetItemByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<DictionaryItemEntity?>(null);
        public Task<IReadOnlyList<DictionaryDefinitionEntity>> GetDefinitionsAsync(int skip = 0, int take = 20, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DictionaryDefinitionEntity>>(Array.Empty<DictionaryDefinitionEntity>());
        public Task<IReadOnlyList<DictionaryItemEntity>> GetItemsAsync(long definitionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DictionaryItemEntity>>(Array.Empty<DictionaryItemEntity>());
        public Task UpsertDefinitionAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertItemAsync(DictionaryItemEntity item, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDefinitionAsync(long id, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteItemAsync(long id, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>测试专用 IDictionaryManager：标记消费方自定义实现。</summary>
    private sealed class ConsumerDictionaryManager : IDictionaryManager
    {
        public Task<DictionaryDefinitionEntity?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default) => Task.FromResult<DictionaryDefinitionEntity?>(null);
        public Task<IReadOnlyList<DictionaryItemEntity>> GetItemsAsync(string code, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DictionaryItemEntity>>(Array.Empty<DictionaryItemEntity>());
        public Task<DictionaryDefinitionWithItems?> GetDefinitionWithItemsAsync(string code, CancellationToken ct = default)
            => Task.FromResult<DictionaryDefinitionWithItems?>(null);
        public Task UpsertDefinitionAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertItemAsync(DictionaryItemEntity item, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDefinitionAsync(long id, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteItemAsync(long id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DictionaryTreeNode>> GetItemsTreeAsync(string code, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DictionaryTreeNode>>(Array.Empty<DictionaryTreeNode>());
    }
}