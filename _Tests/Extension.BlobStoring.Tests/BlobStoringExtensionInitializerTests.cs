using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobStoringExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、二态 DI 注册形态：
/// <list type="bullet">
/// <item><see cref="IBlobStorageService"/>（Abstractions 契约非 IDomainService——接线型基础设施）——TryAddScoped 普通 DI（ImplementationType）；</item>
/// <item><see cref="IBlobRecordStore"/>（领域服务门面 <c>: IDomainService</c>）——AddConstructibleService（接口可构造守卫工厂 + 实现类 throw-factory + 域外解析抛）。</item>
/// </list>
/// <para>V4.10.53（领域自治根治）：注册形态由 <c>TryAddScoped&lt;IBlobRecordStore, BlobRecordStore&gt;</c> 改为
/// <c>AddConstructibleService</c>——消费方统一经 <c>User.Use&lt;IBlobRecordStore&gt;()</c> 解析（AOP 路径）。</para>
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

    // ── 接线型基础设施：IBlobStorageService → TryAddScoped（ImplementationType） ──

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

    // ── 领域服务门面：IBlobRecordStore → AddConstructibleService（守卫工厂 + throw-factory + 域外抛） ──

    [Fact]
    public void ConfigureServices_Registers_IBlobRecordStore_GuardFactory()
    {
        var services = new ServiceCollection();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口注册为可构造守卫工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBlobRecordStore));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_BlobRecordStore_ThrowFactory()
    {
        var services = new ServiceCollection();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<IBlobRecordStore>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(BlobRecordStore));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<BlobRecordStore>());
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // V4.10.53：接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IBlobRecordStore>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IBlobRecordStore", ex.Message);
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
}
