using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Utility.DataPort;
using TKW.Framework.Utility.DataPort.Providers.MiniExcel;

namespace TKWF.Ext.DataPort.Tests;

/// <summary>
/// DataPortExtensionInitializer 测试——DI 注册（核心服务/Provider/任务服务）+ Options 默认值。
/// <para>注意：IImportService/IExportService/IImportProvider/IExportProvider 经 Factory 注册
/// （sp.GetRequiredService&lt;具体实现&gt;）——经 Descriptor 验证 ServiceType + Lifetime；
/// 具体实现类（ImportService/MiniExcelImportProvider）仍保留 ImplementationType 可断言。</para>
/// </summary>
public class DataPortExtensionInitializerTests
{
    private const string TkfwDataPortSection = "TKWF:DataPort";

    [Fact]
    public void ConfigureServices_RegistersImportService()
    {
        var services = new ServiceCollection();
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(IImportService));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_RegistersExportService()
    {
        var services = new ServiceCollection();
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(IExportService));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_RegistersImportProvider()
    {
        var services = new ServiceCollection();
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var providerDescriptor = services.First(d => d.ServiceType == typeof(IImportProvider));
        var concreteDescriptor = services.First(d => d.ServiceType == typeof(MiniExcelImportProvider));

        Assert.Equal(ServiceLifetime.Singleton, providerDescriptor.Lifetime);
        Assert.NotNull(providerDescriptor.ImplementationFactory);
        Assert.Equal(typeof(MiniExcelImportProvider), concreteDescriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, concreteDescriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_RegistersExportProvider()
    {
        var services = new ServiceCollection();
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var providerDescriptor = services.First(d => d.ServiceType == typeof(IExportProvider));
        var concreteDescriptor = services.First(d => d.ServiceType == typeof(MiniExcelExportProvider));

        Assert.Equal(ServiceLifetime.Singleton, providerDescriptor.Lifetime);
        Assert.NotNull(providerDescriptor.ImplementationFactory);
        Assert.Equal(typeof(MiniExcelExportProvider), concreteDescriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, concreteDescriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_RegistersDataImportTaskService()
    {
        var services = new ServiceCollection();
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(IDataImportTaskService));

        Assert.Equal(typeof(DataImportTaskService), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_RegistersOptions()
    {
        var services = new ServiceCollection();
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(Microsoft.Extensions.Options.IConfigureOptions<DataPortOptions>));

        Assert.NotNull(descriptor);
    }

    [Fact]
    public void ConfigureServices_OptionsHasDefaultValues()
    {
        // DataPortOptions 默认值验证（BindConfiguration("TKWF:DataPort") 在消费方环境中生效，测试仅验证默认值）
        var options = new DataPortOptions();

        Assert.Equal(500, options.DefaultBatchSize);
        Assert.False(options.StopOnBatchFailure);
        Assert.Equal("miniexcel", options.DefaultProvider);
    }

    [Fact]
    public async Task ConfigureOptions_BindsConfigurationSection()
    {
        // 验证 BindConfiguration("TKWF:DataPort") 生效——配置覆盖默认值
        var services = new ServiceCollection();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TkfwDataPortSection}:{nameof(DataPortOptions.DefaultBatchSize)}"] = "10",
                [$"{TkfwDataPortSection}:{nameof(DataPortOptions.StopOnBatchFailure)}"] = "true",
                [$"{TkfwDataPortSection}:{nameof(DataPortOptions.DefaultProvider)}"] = "csv"
            })
            .Build();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);

        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DataPortOptions>>().Value;

        Assert.Equal(10, options.DefaultBatchSize);
        Assert.True(options.StopOnBatchFailure);
        Assert.Equal("csv", options.DefaultProvider);
    }
}