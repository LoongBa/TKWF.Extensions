using System.Text;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// MetricDefinitionLoader 测试——规格文件结构校验。
/// </summary>
public class MetricDefinitionLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public MetricDefinitionLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tkwf-metrics-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteSpec(string json)
    {
        var path = Path.Combine(_tempDir, "metric-definitions.json");
        File.WriteAllText(path, json, Encoding.UTF8);
        return path;
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

    [Fact]
    public void Load_ValidSpec_ReturnsDefinitions()
    {
        var definitions = MetricDefinitionLoader.Load(WriteSpec(ValidSpec));

        var definition = Assert.Single(definitions);
        Assert.Equal("aov-cny", definition.Name);
        Assert.Equal("ratio", definition.Calculator);
        Assert.Equal("TotalAmount", definition.Parameters["numeratorField"]);
    }

    [Fact]
    public void Load_MalformedJson_Throws()
    {
        var ex = Assert.Throws<MetricDefinitionException>(() =>
            MetricDefinitionLoader.Load(WriteSpec("{ not-valid-json ")));

        Assert.Contains("JSON", ex.Message);
    }

    [Fact]
    public void Load_DuplicateMetricName_Throws()
    {
        var spec = """
        {
          "$schema": "tkwf-metrics-definitions/v1",
          "specKey": "s1",
          "metrics": [
            { "name": "dup", "calculator": "ratio", "parameters": { "a": "b" } },
            { "name": "dup", "calculator": "ratio", "parameters": { "a": "b" } }
          ]
        }
        """;

        var ex = Assert.Throws<MetricDefinitionException>(() =>
            MetricDefinitionLoader.Load(WriteSpec(spec)));

        Assert.Contains("重复", ex.Message);
    }

    [Fact]
    public void Load_WrongSchema_Throws()
    {
        var spec = ValidSpec.Replace("tkwf-metrics-definitions/v1", "tkwf-other/v9", StringComparison.Ordinal);

        Assert.Throws<MetricDefinitionException>(() =>
            MetricDefinitionLoader.Load(WriteSpec(spec)));
    }

    [Fact]
    public void Load_NonStringParameterValue_Throws()
    {
        var spec = """
        {
          "$schema": "tkwf-metrics-definitions/v1",
          "specKey": "s1",
          "metrics": [
            { "name": "bad", "calculator": "ratio", "parameters": { "windowDays": 30 } }
          ]
        }
        """;

        Assert.Throws<MetricDefinitionException>(() =>
            MetricDefinitionLoader.Load(WriteSpec(spec)));
    }

    [Fact]
    public void Load_MissingSpecKey_Throws()
    {
        var spec = ValidSpec.Replace("\"specKey\": \"PaymentLogStatView--daily-sales-trend\",\n", "", StringComparison.Ordinal);

        var ex = Assert.Throws<MetricDefinitionException>(() =>
            MetricDefinitionLoader.Load(WriteSpec(spec)));

        Assert.Contains("specKey", ex.Message);
    }

    [Fact]
    public void Load_MissingParametersObject_Throws()
    {
        var spec = """
        {
          "$schema": "tkwf-metrics-definitions/v1",
          "specKey": "s1",
          "metrics": [
            { "name": "bad", "calculator": "ratio" }
          ]
        }
        """;

        var ex = Assert.Throws<MetricDefinitionException>(() =>
            MetricDefinitionLoader.Load(WriteSpec(spec)));

        Assert.Contains("parameters", ex.Message);
    }

    [Fact]
    public void Load_EmptyMetrics_ReturnsEmpty()
    {
        var spec = """
        {
          "$schema": "tkwf-metrics-definitions/v1",
          "specKey": "s1",
          "metrics": []
        }
        """;

        Assert.Empty(MetricDefinitionLoader.Load(WriteSpec(spec)));
    }
}
