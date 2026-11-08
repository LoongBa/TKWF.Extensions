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
            provider.Load("merchant", "PaymentLogStatView--no-such"));

        Assert.Contains("不存在", ex.Message);
    }

    [Fact]
    public void Load_DoesNotRequireD20Manifest_Placeholder()
    {
        // D20 manifest 状态校验为占位（C2，依赖 D20 落地、尚未实施）——无 manifest.json 仍应加载成功
        WriteSpecAt("merchant", "PaymentLogStatView--daily-sales-trend");
        var provider = CreateProvider(_tempDir);

        var definitions = provider.Load("merchant", "PaymentLogStatView--daily-sales-trend");

        Assert.Single(definitions);
    }

    [Fact]
    public void Load_MalformedSpec_PropagatesLoaderValidation()
    {
        var dir = Path.Combine(_tempDir, "merchant", "PaymentLogStatView--bad");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "metric-definitions.json"), "{ broken json ", Encoding.UTF8);
        var provider = CreateProvider(_tempDir);

        Assert.Throws<MetricDefinitionException>(() => provider.Load("merchant", "PaymentLogStatView--bad"));
    }

    // ── B 档 M-01：domain/specKey 命名正则校验（对齐 Analytics 先例）——穿越/非法标识符拒绝 ──

    [Theory]
    [InlineData("../x", "PaymentLogStatView--daily-sales-trend")]      // domain 穿越段
    [InlineData("merchant", "../PaymentLogStatView--daily-sales-trend")] // specKey 穿越段
    [InlineData("C:merchant", "PaymentLogStatView--daily-sales-trend")] // domain 冒号（盘符/URI）
    [InlineData("merchant/x", "PaymentLogStatView--daily-sales-trend")] // domain 路径分隔符
    [InlineData("merchant", "PaymentLogStatView--daily-sales-trend/..")] // specKey 绝对路径
    [InlineData("merchant", "PaymentLogStatView--daily-sales-trend\\x")] // specKey 反斜杠
    [InlineData("Merchant", "PaymentLogStatView--daily-sales-trend")]   // domain 大写（非法规范）
    [InlineData("merchant", "paymentlogstatview--daily-sales-trend")]   // specKey 非 PascalCase 首段
    public void Load_IllegalDomainOrSpecKey_Throws(string domain, string specKey)
    {
        var provider = CreateProvider(_tempDir);

        var ex = Assert.Throws<MetricDefinitionException>(() => provider.Load(domain, specKey));

        Assert.Contains("非法", ex.Message);
    }
}
