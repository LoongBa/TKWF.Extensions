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
/// DataDictionaryExtensionInitializer 测试（V0.3.0 领域自治根治重写）——
/// [TKWFExtension] 特性声明、AddConstructibleService 注册形态（接口守卫工厂 + 实现类 throw-factory + 域外抛）、
/// IMemoryCache + Options 注册。
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
    public void ConfigureServices_Registers_IDictionaryStore_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        // V0.3.0：AddConstructibleService——接口注册为构造工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDictionaryStore));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IDictionaryManager_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDictionaryManager));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_DictionaryStore_ThrowFactory()
    {
        var services = new ServiceCollection();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<IDictionaryStore>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DictionaryStore));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<DictionaryStore>());
    }

    [Fact]
    public void ConfigureServices_Registers_DictionaryManager_ThrowFactory()
    {
        var services = new ServiceCollection();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DictionaryManager));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<DictionaryManager>());
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // V0.3.0：接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new DataDictionaryExtensionInitializer<DataDictionaryUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var storeEx = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDictionaryStore>());
        Assert.Contains("领域架构守卫", storeEx.Message);
        Assert.Contains("IDictionaryStore", storeEx.Message);

        var managerEx = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDictionaryManager>());
        Assert.Contains("领域架构守卫", managerEx.Message);
        Assert.Contains("IDictionaryManager", managerEx.Message);
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
}
