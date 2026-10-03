using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Utility.DataPort;
using TKW.Framework.Utility.DataPort.Providers.MiniExcel;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.DataPort.Tests;

/// <summary>
/// DataPortExtensionInitializer 测试——DI 注册（核心服务/Provider/任务服务）+ Options 默认值。
/// <para>注意：IImportService/IExportService/IImportProvider/IExportProvider 经 Factory 注册
/// （sp.GetRequiredService&lt;具体实现&gt;）——经 Descriptor 验证 ServiceType + Lifetime；
/// 具体实现类（ImportService/MiniExcelImportProvider）仍保留 ImplementationType 可断言。</para>
/// <para>V4.10.53（领域自治根治，ADR90，正确路线）：<see cref="IDataImportTaskService"/> 注册形态由
/// TryAddScoped（ImplementationType）改为 <b>AddConstructibleService</b>——接口 = 可构造守卫工厂 + 实现类 = throw-factory；
/// 域作用域外直接 DI 解析接口必抛领域架构守卫（DI004 运行期兜底）。</para>
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
    public void ConfigureServices_RegistersDataImportTaskService_GuardFactory()
    {
        var services = new ServiceCollection();
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口注册为可构造守卫工厂（非实现映射），Scoped 生命周期
        var descriptor = services.First(d => d.ServiceType == typeof(IDataImportTaskService));

        Assert.Null(descriptor.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_RegistersDataImportTaskService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<IDataImportTaskService>() 创建
        var descriptor = services.First(d => d.ServiceType == typeof(DataImportTaskService));
        Assert.NotNull(descriptor.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<DataImportTaskService>());
    }

    [Fact]
    public void DataImportTaskService_OutsideUseScope_Throws()
    {
        // AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDataImportTaskService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IDataImportTaskService", ex.Message);
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