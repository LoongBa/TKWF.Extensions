using System.Text;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// MetricsSpecFileProvider 测试——SpecRoot 解析、文件缺失、manifest 占位、结构校验透传。
/// </summary>
public class MetricsSpecFileProviderTests : IDisposable
{
    private readonly string _tempDir;

    public MetricsSpecFileProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tkwf-metrics-spec", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private const string ValidSpec = """
        {
          "$schema": "tkwf-metrics-definitions/v1",
          "specKey": "PaymentLogStatView--daily-sales-trend",
          "metrics": [
            { "name": "aov-cny", "calculator": "ratio",
              "parameters": { "numeratorField": "TotalAmount", "numeratorAggregate": "sum",
                              "denominatorField": "PaidCount", "denominatorAggregate": "count" } }
          ]
        }
        """;

    private MetricsSpecFileProvider CreateProvider(string specRoot)
        => new(Options.Create(new MetricsOptions { SpecRoot = specRoot }));

    private string WriteSpecAt(string domain, string specKey)
    {
        var dir = Path.Combine(_tempDir, domain, specKey);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "metric-definitions.json");
        File.WriteAllText(path, ValidSpec, Encoding.UTF8);
        return path;
    }

    [Fact]
    public void Load_ResolvesSpecRootAndLoads()
    {
        WriteSpecAt("merchant", "PaymentLogStatView--daily-sales-trend");
        var provider = CreateProvider(_tempDir);

        var definitions = provider.Load("merchant", "PaymentLogStatView--daily-sales-trend");

        var definition = Assert.Single(definitions);
        Assert.Equal("aov-cny", definition.Name);
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        var provider = CreateProvider(_tempDir);

        var ex = Assert.Throws<MetricDefinitionException>(() =>
            provider.Load("merchant", "NoSuchSpec"));

        Assert.Contains("不存在", ex.Message);
    }

    [Fact]
    public void Load_DoesNotRequireD20Manifest_Placeholder()
    {
        // v0.2.0 才做 manifest 状态校验（C2 占位）——无 manifest.json 仍应加载成功
        WriteSpecAt("merchant", "PaymentLogStatView--daily-sales-trend");
        var provider = CreateProvider(_tempDir);

        var definitions = provider.Load("merchant", "PaymentLogStatView--daily-sales-trend");

        Assert.Single(definitions);
    }

    [Fact]
    public void Load_MalformedSpec_PropagatesLoaderValidation()
    {
        var dir = Path.Combine(_tempDir, "merchant", "BadSpec");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "metric-definitions.json"), "{ broken json ", Encoding.UTF8);
        var provider = CreateProvider(_tempDir);

        Assert.Throws<MetricDefinitionException>(() => provider.Load("merchant", "BadSpec"));
    }
}
