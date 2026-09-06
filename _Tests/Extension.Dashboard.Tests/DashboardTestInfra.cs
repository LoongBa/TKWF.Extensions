using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKWF.Ext.Dashboard;

namespace TKWF.Ext.Dashboard.Tests;

/// <summary>测试数据行——模拟 DMP-Lite 支付数据字段子集。</summary>
public class TestDataRow
{
    public long? MemberId { get; set; }
    public DateTime? BizDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public int? PaidCount { get; set; }
    public string? Stage { get; set; }

    public static TestDataRow Row(long memberId, DateTime bizDate, decimal amount, int paidCount = 1, string? stage = null)
        => new() { MemberId = memberId, BizDate = bizDate, TotalAmount = amount, PaidCount = paidCount, Stage = stage };
}

/// <summary>测试数据源——按字段名反射访问（模拟消费方 IDashboardDataProvider 实现）。</summary>
public sealed class TestDashboardDataProvider : IDashboardDataProvider
{
    public const string ProviderName = "test-ds";

    private readonly IReadOnlyList<object> _rows;
    private readonly IReadOnlyDictionary<string, object?>? _receivedFilters;

    public TestDashboardDataProvider(IEnumerable<TestDataRow> rows)
    {
        _rows = rows.Cast<object>().ToArray();
    }

    public TestDashboardDataProvider(IEnumerable<TestDataRow> rows, IReadOnlyDictionary<string, object?>? receivedFilters)
    {
        _rows = rows.Cast<object>().ToArray();
        _receivedFilters = receivedFilters;
    }

    public string Name => ProviderName;

    /// <summary>测试断言：最近一次取数收到的 filters（验证透传）。</summary>
    public IReadOnlyDictionary<string, object?>? ReceivedFilters { get; private set; }

    public Task<(IReadOnlyList<object> Rows, Func<object, string, object?> Accessor)> GetDataAsync(
        string widgetName,
        IReadOnlyDictionary<string, object?>? filters,
        CancellationToken ct = default)
    {
        ReceivedFilters = filters;
        ct.ThrowIfCancellationRequested();

        Func<object, string, object?> accessor = (source, fieldName) =>
            source.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source);
        return Task.FromResult((Rows: (IReadOnlyList<object>)_rows, Accessor: accessor));
    }
}

/// <summary>测试基础设施——临时目录 + 规格文件写入 + DI 构建。</summary>
internal static class DashboardTestInfra
{
    /// <summary>创建独立临时目录（每个测试唯一，防串扰）。</summary>
    public static TempSpecRoot CreateTempRoot()
        => new(Path.Combine(Path.GetTempPath(), "dashboard-tests", Guid.NewGuid().ToString("N")));

    /// <summary>写 Dashboard 定义文件：{specRoot}/{group?}/{dashKey}.json。</summary>
    public static string WriteDashboard(string specRoot, string dashKey, string json, string? group = null)
    {
        var dir = group != null ? Path.Combine(specRoot, group) : specRoot;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{dashKey}.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>写 Metrics 规格文件：{metricsSpecRoot}/{domain}/{specKey}/metric-definitions.json。</summary>
    public static string WriteMetricsSpec(string metricsSpecRoot, string domain, string specKey, string json)
    {
        var dir = Path.Combine(metricsSpecRoot, domain, specKey);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "metric-definitions.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>标准 dashboard.json 定义（含 metricRef + dataSource Widget）。</summary>
    public static string DashboardJson(string dashKey = "overview") => $$"""
        {
          "name": "{{dashKey}}",
          "title": "经营总览",
          "widgets": [
            { "name": "today-revenue", "type": "numberContainer", "row": 0, "order": 0, "width": 1,
              "dataSource": "test-ds", "metricRef": "merchant/payment-metrics:total-revenue" },
            { "name": "payment-list", "type": "list", "row": 1, "order": 0, "width": 2,
              "dataSource": "test-ds" }
          ]
        }
        """;

    /// <summary>标准 Metrics 规格（total-revenue 单值 + daily-trend time-bucket 多切片）。</summary>
    public static string MetricsSpecJson() => """
        {
          "$schema": "tkwf-metrics-definitions/v1",
          "specKey": "payment-metrics",
          "metrics": [
            { "name": "total-revenue", "calculator": "ratio",
              "parameters": { "numeratorField": "TotalAmount", "numeratorAggregate": "sum",
                              "denominatorField": "PaidCount", "denominatorAggregate": "count" } },
            { "name": "daily-trend", "calculator": "time-bucket",
              "parameters": { "timeField": "BizDate", "bucket": "day",
                              "valueField": "TotalAmount", "aggregate": "sum" } }
          ]
        }
        """;

    /// <summary>构建 Dashboard 服务容器（真实 IMetricCalculatorFactory + 测试数据源）。</summary>
    public static (IServiceProvider Sp, TestDashboardDataProvider Provider) BuildServices(
        string dashboardSpecRoot,
        string metricsSpecRoot,
        IReadOnlyList<TestDataRow> rows,
        bool registerMetricsFactory = true)
    {
        var provider = new TestDashboardDataProvider(rows);
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddOptions<DashboardOptions>().Configure(o => o.SpecRoot = dashboardSpecRoot);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TKWF:Metrics:SpecRoot"] = metricsSpecRoot
            })
            .Build());
        services.AddSingleton<DashboardSpecFileProvider>();
        services.AddScoped<IDashboardDataService, DashboardDataService>();
        services.AddSingleton<IDashboardDataProvider>(provider);

        if (registerMetricsFactory)
            services.TryAddSingleton<IMetricCalculatorFactory, CalculatorFactory>();

        return (services.BuildServiceProvider(), provider);
    }
}

/// <summary>临时规格根目录（IDisposable 清理）。</summary>
public sealed class TempSpecRoot : IDisposable
{
    public string Path { get; }

    public TempSpecRoot(string path)
    {
        Path = path;
        Directory.CreateDirectory(path);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
        catch (IOException) { }
    }
}
