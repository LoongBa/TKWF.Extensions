using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKWF.Ext.HealthCheck;

namespace TKWF.Ext.HealthCheck.Tests;

/// <summary>
/// HealthCheck 扩展接线测试——[TKWFExtension] 声明 / ConfigureServices Options 注册 /
/// AddTkfwHealthChecks 服务注册 / TKWF:HealthCheck 配置节绑定 / configure 委托覆盖。
/// </summary>
public class HealthCheckRegistrationTests
{
    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(HealthCheckExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("HealthCheck", attr!.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_Options_WithDefaults()
    {
        var services = new ServiceCollection();
        // IOptions 解析依赖 IConfiguration（BindConfiguration 的 Configure<IConfiguration>）
        services.AddSingleton<IConfiguration>(EmptyConfiguration());
        new HealthCheckExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<HealthCheckEndpointOptions>>().Value;

        Assert.Equal("/health", options.Path);
        Assert.True(options.Enabled);
        Assert.False(options.Detailed);
        Assert.True(options.AllowAnonymous);
    }

    [Fact]
    public void AddTkfwHealthChecks_Registers_HealthCheckService_And_Options()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(EmptyConfiguration());
        // 裸 ServiceCollection 需显式日志基础设施（DefaultHealthCheckService 依赖 ILogger<>；真实 host 默认已注册）
        services.AddLogging();
        services.AddTkfwHealthChecks();

        var sp = services.BuildServiceProvider();

        // net10 内置 HealthChecks 服务（AddHealthChecks 展开）
        Assert.NotNull(sp.GetService<HealthCheckService>());

        // Options 默认值
        var options = sp.GetRequiredService<IOptions<HealthCheckEndpointOptions>>().Value;
        Assert.Equal("/health", options.Path);
        Assert.True(options.Enabled);
        Assert.False(options.Detailed);
        Assert.True(options.AllowAnonymous);
    }

    [Fact]
    public void AddTkfwHealthChecks_Configure_Overrides_ConfigSection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TKWF:HealthCheck:Path"] = "/from-config",
                ["TKWF:HealthCheck:Detailed"] = "true",
                ["TKWF:HealthCheck:AllowAnonymous"] = "false"
            })
            .Build());
        services.AddTkfwHealthChecks(o =>
        {
            o.Detailed = false; // configure 委托后于配置节绑定应用——代码覆盖配置
        });

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<HealthCheckEndpointOptions>>().Value;

        Assert.Equal("/from-config", options.Path); // 配置节绑定生效（configure 未覆盖的键）
        Assert.False(options.Detailed);             // configure 覆盖配置节
        Assert.False(options.AllowAnonymous);       // 配置节绑定生效
    }

    [Fact]
    public void Options_Bound_From_TKWFHealthCheck_Section()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TKWF:HealthCheck:Path"] = "/hc",
                ["TKWF:HealthCheck:Enabled"] = "false",
                ["TKWF:HealthCheck:Detailed"] = "true",
                ["TKWF:HealthCheck:AllowAnonymous"] = "false"
            })
            .Build());
        services.AddTkfwHealthChecks();

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<HealthCheckEndpointOptions>>().Value;

        Assert.Equal("/hc", options.Path);
        Assert.False(options.Enabled);
        Assert.True(options.Detailed);
        Assert.False(options.AllowAnonymous);
    }
}
