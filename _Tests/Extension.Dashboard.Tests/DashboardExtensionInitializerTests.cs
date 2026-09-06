using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKWF.Ext.Dashboard;

namespace TKWF.Ext.Dashboard.Tests;

/// <summary>
/// DashboardExtensionInitializer 测试——DI 注册（SpecProvider Singleton + DataService Scoped）+ Options 默认值。
/// </summary>
public class DashboardExtensionInitializerTests
{
    [Fact]
    public void ConfigureServices_RegistersDataService()
    {
        var services = new ServiceCollection();
        new DashboardExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(IDashboardDataService));

        Assert.Equal(typeof(DashboardDataService), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_RegistersSpecProvider()
    {
        var services = new ServiceCollection();
        new DashboardExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(DashboardSpecFileProvider));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_OptionsHasDefaultValues()
    {
        // DashboardOptions 默认值验证（BindConfiguration 在消费方环境生效，测试仅验证默认值兜底）
        var options = new DashboardOptions();

        Assert.Equal("docs/dashboard-specs", options.SpecRoot);
    }
}
