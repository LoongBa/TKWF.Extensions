using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// PrintTemplatesExtensionInitializer 测试（V0.3.0 领域自治根治重写）——
/// AddConstructibleService 注册形态（接口守卫工厂 + 实现类 throw-factory + 域外抛）、
/// Renderer 接线型 TryAddSingleton、Options 默认值。
/// </summary>
public class PrintTemplatesExtensionInitializerTests
{
    [Fact]
    public void ConfigureServices_Registers_ITemplateStore_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new PrintTemplatesExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // V0.3.0：AddConstructibleService——接口注册为构造工厂（非实现映射），Scoped 生命周期
        var descriptor = services.First(d => d.ServiceType == typeof(ITemplateStore));

        Assert.Null(descriptor.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_ITemplateManager_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new PrintTemplatesExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(ITemplateManager));

        Assert.Null(descriptor.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_TemplateStore_ThrowFactory()
    {
        var services = new ServiceCollection();
        new PrintTemplatesExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<ITemplateStore>() 创建
        var descriptor = services.First(d => d.ServiceType == typeof(TemplateStore));
        Assert.NotNull(descriptor.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<TemplateStore>());
    }

    [Fact]
    public void ConfigureServices_Registers_TemplateManager_ThrowFactory()
    {
        var services = new ServiceCollection();
        new PrintTemplatesExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(TemplateManager));
        Assert.NotNull(descriptor.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<TemplateManager>());
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // V0.3.0：接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new PrintTemplatesExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var storeEx = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ITemplateStore>());
        Assert.Contains("领域架构守卫", storeEx.Message);
        Assert.Contains("ITemplateStore", storeEx.Message);

        var managerEx = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ITemplateManager>());
        Assert.Contains("领域架构守卫", managerEx.Message);
        Assert.Contains("ITemplateManager", managerEx.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_ITemplateRenderer_SingletonWiring()
    {
        // Renderer 为接线型基础设施（无 user 依赖）——保持 TryAddSingleton 普通 DI 注册（非 AddConstructibleService）
        var services = new ServiceCollection();
        new PrintTemplatesExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(ITemplateRenderer));

        Assert.Equal(typeof(ScribanTemplateRenderer), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_OptionsHasDefaultValues()
    {
        // PrintTemplatesOptions 默认值验证（无需 DI 解析——直接验证 Options 类默认值）
        // BindConfiguration("TKWF:PrintTemplates") 在消费方环境中生效，测试仅验证默认值
        var options = new PrintTemplatesOptions();

        Assert.Equal(1000, options.LoopLimit);
        Assert.Equal(100, options.RecursiveLimit);
        Assert.Equal(1048576, options.LimitToString);
        Assert.Equal(10000, options.RegexTimeOut);
    }
}
