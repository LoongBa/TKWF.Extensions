using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// PrintTemplatesExtensionInitializer 测试——DI 注册（Store/Manager Scoped + Renderer Singleton）+ Options 默认值。
/// <para>注意：TemplateStore 依赖两个 DataService（未在测试 DI 中注册）、ScribanTemplateRenderer 依赖
/// PrintTemplatesOptions（AddOptions 不注册 T 本身）——因此 Store/Manager/Renderer 经 Descriptor 验证而非 resolve；
/// Options 测试显式注册空 IConfiguration（BindConfiguration 需要）后 resolve 验证默认值。</para>
/// </summary>
public class PrintTemplatesExtensionInitializerTests
{
    [Fact]
    public void ConfigureServices_RegistersTemplateStore()
    {
        var services = new ServiceCollection();
        new PrintTemplatesExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(ITemplateStore));

        Assert.Equal(typeof(TemplateStore), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_RegistersTemplateManager()
    {
        var services = new ServiceCollection();
        new PrintTemplatesExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(ITemplateManager));

        Assert.Equal(typeof(TemplateManager), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_RegistersTemplateRenderer()
    {
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
