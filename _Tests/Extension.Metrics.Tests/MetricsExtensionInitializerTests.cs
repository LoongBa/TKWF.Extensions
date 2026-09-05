using System.Reflection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Utility.Metrics;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// MetricsExtensionInitializer 测试——[TKWFExtension] 特性声明、DI 注册（工厂 + Singleton）、TryAdd 语义。
/// </summary>
public class MetricsExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(MetricsExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Metrics", attr.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_IMetricsEngine_SingletonFactory()
    {
        var services = new ServiceCollection();
        new MetricsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMetricsEngine));

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory); // C3：工厂 lambda 桥接 MetricsOptions → MetricsEngineOptions
    }

    [Fact]
    public void ConfigureServices_Registers_IMetricCalculatorFactory_Singleton()
    {
        var services = new ServiceCollection();
        new MetricsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMetricCalculatorFactory));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(CalculatorFactory), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAdd_DoesNotOverrideConsumerEngine()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMetricsEngine, ConsumerMetricsEngine>();
        new MetricsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IMetricsEngine)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerMetricsEngine), descriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_ResolvesEngine_FromContainer()
    {
        var services = new ServiceCollection();
        new MetricsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddOptions<MetricsOptions>(); // 兜底默认值（无 IConfiguration）

        using var provider = services.BuildServiceProvider();
        var engine = provider.GetService<IMetricsEngine>();
        var factory = provider.GetService<IMetricCalculatorFactory>();
        var options = provider.GetService<IOptions<MetricsOptions>>();

        Assert.NotNull(engine);
        Assert.NotNull(factory);
        Assert.Equal("docs/analytics-specs", options!.Value.SpecRoot);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Value.CalculateTimeout);
    }

    /// <summary>测试专用 IMetricsEngine：标记消费方自定义实现。</summary>
    private sealed class ConsumerMetricsEngine : IMetricsEngine
    {
        public Task<IReadOnlyList<MetricResult>> CalculateAsync<T>(
            IReadOnlyList<T> data, IReadOnlyList<MetricDefinition> definitions, CancellationToken ct = default)
            where T : class
            => Task.FromResult<IReadOnlyList<MetricResult>>(Array.Empty<MetricResult>());
    }
}
