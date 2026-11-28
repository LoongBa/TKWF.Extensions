using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobStoringExtensionInitializer 测试（A5 Store-debt cleanup 后）——接线型基础设施 IBlobStorageService 注册形态。
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
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStorageService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IBlobStorageService, ConsumerBlobStorageService>();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        var serviceDescriptors = services.Where(d => d.ServiceType == typeof(IBlobStorageService)).ToList();
        Assert.Single(serviceDescriptors);
        Assert.Equal(typeof(ConsumerBlobStorageService), serviceDescriptors[0].ImplementationType);
    }

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
}
