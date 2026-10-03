using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings.Tests;

/// <summary>
/// SettingsExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、AddConstructibleService 注册形态
/// （接口构造工厂 + 实现类 throw-factory）、CurrentAopUser 域作用域守卫、IMemoryCache + Options 注册。
/// <para>V0.3.0（领域自治根治）：注册形态由 TryAddScoped 改为 <c>AddConstructibleService</c>——
/// 消费方统一经 <c>User.Use&lt;ISettingManager&gt;()</c> 解析（AOP 路径先设 CurrentAopUser 再 GetRequiredService）。</para>
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
    public void ConfigureServices_Registers_ISettingManager_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        // V0.3.0：AddConstructibleService——接口注册为构造工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISettingManager));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_SettingManager_ThrowFactory()
    {
        var services = new ServiceCollection();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<ISettingManager>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(SettingManager));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SettingManager>());
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // V0.3.0：接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISettingManager>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ISettingManager", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_IMemoryCache_Descriptor()
    {
        var services = new ServiceCollection();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMemoryCache));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(MemoryCache), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_SettingsOptions_Descriptor()
    {
        var services = new ServiceCollection();
        new SettingsExtensionInitializer<SettingsUserInfo>().ConfigureServices(services);

        // AddOptions<T> 注册 Options 基础设施，可通过 ServiceProvider 解析 IOptions<T>
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SettingsOptions>>();

        Assert.NotNull(options);
        Assert.Equal("Global", options.Value.DefaultSettingValueProvider);
        Assert.Equal(300, options.Value.CacheExpirationSeconds);
    }
}
