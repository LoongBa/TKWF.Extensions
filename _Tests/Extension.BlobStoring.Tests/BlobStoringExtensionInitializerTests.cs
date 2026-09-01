using System;
using System.Linq;
using System.Reflection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobStoringExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、DI 注册、TryAddScoped 语义。
/// </summary>
public class BlobStoringExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(BlobStoringExtensionInitializer<BlobStoringUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("BlobStoring", attr.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_IBlobStorageService_Descriptor()
    {
        var services = new ServiceCollection();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBlobStorageService));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(LocalStorageService), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IBlobRecordStore_Descriptor()
    {
        var services = new ServiceCollection();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBlobRecordStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(FreeSqlBlobRecordStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStorageService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IBlobStorageService, ConsumerBlobStorageService>();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        var serviceDescriptors = services.Where(d => d.ServiceType == typeof(IBlobStorageService)).ToList();
        Assert.Single(serviceDescriptors);
        Assert.Equal(typeof(ConsumerBlobStorageService), serviceDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerRecordStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<IBlobRecordStore, ConsumerBlobRecordStore>();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        var storeDescriptors = services.Where(d => d.ServiceType == typeof(IBlobRecordStore)).ToList();
        Assert.Single(storeDescriptors);
        Assert.Equal(typeof(ConsumerBlobRecordStore), storeDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_Registers_ScopedLifecycle()
    {
        var services = new ServiceCollection();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        var serviceDesc = services.First(d => d.ServiceType == typeof(IBlobStorageService));
        var storeDesc = services.First(d => d.ServiceType == typeof(IBlobRecordStore));
        Assert.Equal(ServiceLifetime.Scoped, serviceDesc.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, storeDesc.Lifetime);
    }

    /// <summary>测试专用 IBlobStorageService：标记消费方自定义实现。</summary>
    private sealed class ConsumerBlobStorageService : IBlobStorageService
    {
        public Task<BlobInfo?> UploadAsync(string name, System.IO.Stream content, string? contentType = null, CancellationToken ct = default)
            => Task.FromResult<BlobInfo?>(new BlobInfo());
        public Task<System.IO.Stream?> DownloadAsync(string path, CancellationToken ct = default)
            => Task.FromResult<System.IO.Stream?>(null);
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    /// <summary>测试专用 IBlobRecordStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerBlobRecordStore : IBlobRecordStore
    {
        public Task<BlobRecordEntity?> GetAsync(long id, CancellationToken ct = default)
            => Task.FromResult<BlobRecordEntity?>(null);
        public Task<BlobRecordEntity?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<BlobRecordEntity?>(null);
        public Task<IReadOnlyList<BlobRecordEntity>> GetListAsync(string? contentType = null, int skip = 0, int take = 20, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BlobRecordEntity>>(Array.Empty<BlobRecordEntity>());
        public Task SaveAsync(BlobRecordEntity record, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DeleteAsync(long id, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
