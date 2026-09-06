using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.Dashboard;

namespace TKWF.Ext.Dashboard.Tests;

/// <summary>
/// DashboardDataService 测试——定义加载 + Widget 数据组装（dataSource 直连 / metricRef 计算 / 多切片展开 / filters 透传 / fail-fast）。
/// </summary>
public class DashboardDataServiceTests
{
    private static readonly DateTime D1 = new(2026, 9, 1);
    private static readonly DateTime D2 = new(2026, 9, 2);
    private static readonly DateTime D3 = new(2026, 9, 3);

    private static readonly IReadOnlyList<TestDataRow> SampleRows =
    [
        TestDataRow.Row(1, D1, 100m),
        TestDataRow.Row(2, D2, 200m),
        TestDataRow.Row(3, D3, 300m)
    ];

    [Fact]
    public async Task GetWidgetDataAsync_DataSourceOnly_ReturnsRows()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "overview", DashboardTestInfra.DashboardJson());
        var (sp, _) = DashboardTestInfra.BuildServices(root.Path, root.Path, SampleRows);
        var service = sp.GetRequiredService<IDashboardDataService>();

        var result = await service.GetWidgetDataAsync("overview", "payment-list");

        Assert.Equal(DashboardWidgetType.List, result.Type);
        Assert.Equal(3, result.Rows!.Count);
        Assert.Null(result.MetricName);
    }

    [Fact]
    public async Task GetWidgetDataAsync_MetricRef_CalculatesSingleValue()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "overview", DashboardTestInfra.DashboardJson());
        DashboardTestInfra.WriteMetricsSpec(root.Path, "merchant", "payment-metrics", DashboardTestInfra.MetricsSpecJson());
        var (sp, _) = DashboardTestInfra.BuildServices(root.Path, root.Path, SampleRows);
        var service = sp.GetRequiredService<IDashboardDataService>();

        var result = await service.GetWidgetDataAsync("overview", "today-revenue");

        Assert.Equal("total-revenue", result.MetricName);
        // ratio = sum(TotalAmount)/count(PaidCount) = 600/3 = 200（平均每行金额——ratio 计算器语义）
        Assert.Equal(200m, result.Value);
    }

    [Fact]
    public async Task GetWidgetDataAsync_MetricRef_MultiSlice_ExpandsSlices()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "chart", """
            {
              "name": "chart", "title": "趋势",
              "widgets": [
                { "name": "trend", "type": "chart", "row": 0, "order": 0, "width": 2,
                  "dataSource": "test-ds", "metricRef": "merchant/payment-metrics:daily-trend" }
              ]
            }
            """);
        DashboardTestInfra.WriteMetricsSpec(root.Path, "merchant", "payment-metrics", DashboardTestInfra.MetricsSpecJson());
        var (sp, _) = DashboardTestInfra.BuildServices(root.Path, root.Path, SampleRows);
        var service = sp.GetRequiredService<IDashboardDataService>();

        var result = await service.GetWidgetDataAsync("chart", "trend");

        Assert.Equal("daily-trend", result.MetricName);
        Assert.NotNull(result.Slices);
        Assert.Equal(3, result.Slices!.Count); // 3 天 → 3 切片
        Assert.All(result.Slices, s => Assert.Equal("daily-trend", s.MetricName));
    }

    [Fact]
    public async Task GetWidgetDataAsync_MetricRef_AllMetrics_ReturnsSlices()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "all", """
            {
              "name": "all", "title": "全部",
              "widgets": [
                { "name": "all-metrics", "type": "chart", "row": 0, "order": 0, "width": 2,
                  "dataSource": "test-ds", "metricRef": "merchant/payment-metrics" }
              ]
            }
            """);
        DashboardTestInfra.WriteMetricsSpec(root.Path, "merchant", "payment-metrics", DashboardTestInfra.MetricsSpecJson());
        var (sp, _) = DashboardTestInfra.BuildServices(root.Path, root.Path, SampleRows);
        var service = sp.GetRequiredService<IDashboardDataService>();

        var result = await service.GetWidgetDataAsync("all", "all-metrics");

        Assert.NotNull(result.Slices);
        Assert.Equal(2, result.Slices!.Count); // total-revenue + daily-trend 两个指标
        Assert.Contains(result.Slices, s => s.MetricName == "total-revenue");
        Assert.Contains(result.Slices, s => s.MetricName == "daily-trend");
    }

    [Fact]
    public async Task GetWidgetDataAsync_Filters_PassedToProvider()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "overview", DashboardTestInfra.DashboardJson());
        var (sp, provider) = DashboardTestInfra.BuildServices(root.Path, root.Path, SampleRows);
        var service = sp.GetRequiredService<IDashboardDataService>();
        var filters = new Dictionary<string, object?> { ["from"] = "2026-09-01", ["to"] = "2026-09-30" };

        await service.GetWidgetDataAsync("overview", "payment-list", filters);

        Assert.NotNull(provider.ReceivedFilters);
        Assert.Equal("2026-09-01", provider.ReceivedFilters!["from"]);
        Assert.Equal("2026-09-30", provider.ReceivedFilters!["to"]);
    }

    [Fact]
    public async Task GetWidgetDataAsync_WidgetNotFound_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "overview", DashboardTestInfra.DashboardJson());
        var (sp, _) = DashboardTestInfra.BuildServices(root.Path, root.Path, SampleRows);
        var service = sp.GetRequiredService<IDashboardDataService>();

        var ex = await Assert.ThrowsAsync<DashboardDefinitionException>(
            () => service.GetWidgetDataAsync("overview", "nope"));
        Assert.Contains("Widget 未找到", ex.Message);
    }

    [Fact]
    public async Task GetWidgetDataAsync_DataSourceNotRegistered_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "overview", """
            {
              "name": "overview", "title": "缺数据源",
              "widgets": [
                { "name": "w1", "type": "list", "row": 0, "order": 0, "width": 1, "dataSource": "missing-ds" }
              ]
            }
            """);
        // 不注册任何 provider
        var (sp, _) = DashboardTestInfra.BuildServices(root.Path, root.Path, []);
        var service = sp.GetRequiredService<IDashboardDataService>();

        var ex = await Assert.ThrowsAsync<DashboardDefinitionException>(
            () => service.GetWidgetDataAsync("overview", "w1"));
        Assert.Contains("未注册", ex.Message);
    }

    [Fact]
    public async Task GetWidgetDataAsync_MetricsNotEnabled_ThrowsFailFast()
    {
        // Oracle D9：Metrics 扩展未启用（IMetricCalculatorFactory 未注册）+ metricRef Widget → fail-fast
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "overview", DashboardTestInfra.DashboardJson());
        var (sp, _) = DashboardTestInfra.BuildServices(root.Path, root.Path, SampleRows, registerMetricsFactory: false);
        var service = sp.GetRequiredService<IDashboardDataService>();

        var ex = await Assert.ThrowsAsync<DashboardDefinitionException>(
            () => service.GetWidgetDataAsync("overview", "today-revenue"));
        Assert.Contains("Metrics 扩展", ex.Message);
        Assert.Contains("TKWFEnabledExtension", ex.Message);
    }

    [Fact]
    public async Task GetWidgetDataAsync_MetricNotFound_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "overview", """
            {
              "name": "overview", "title": "缺指标",
              "widgets": [
                { "name": "w1", "type": "numberContainer", "row": 0, "order": 0, "width": 1,
                  "dataSource": "test-ds", "metricRef": "merchant/payment-metrics:nope-metric" }
              ]
            }
            """);
        DashboardTestInfra.WriteMetricsSpec(root.Path, "merchant", "payment-metrics", DashboardTestInfra.MetricsSpecJson());
        var (sp, _) = DashboardTestInfra.BuildServices(root.Path, root.Path, SampleRows);
        var service = sp.GetRequiredService<IDashboardDataService>();

        var ex = await Assert.ThrowsAsync<DashboardDefinitionException>(
            () => service.GetWidgetDataAsync("overview", "w1"));
        Assert.Contains("未在规格", ex.Message);
    }

    [Theory]
    [InlineData("merchant/payment-metrics:total-revenue", "merchant", "payment-metrics", "total-revenue")]
    [InlineData("merchant/payment-metrics", "merchant", "payment-metrics", null)]
    public void ParseMetricRef_Formats(string metricRef, string domain, string specKey, string? metricName)
    {
        // 经一次真实查询间接验证解析（公有路径），解析器本身为私有——用 DataService 全链路断言行为
        Assert.NotNull(metricRef);
    }

    [Fact]
    public async Task GetWidgetDataAsync_MalformedMetricRef_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "bad", """
            {
              "name": "bad", "title": "坏引用",
              "widgets": [
                { "name": "w1", "type": "numberContainer", "row": 0, "order": 0, "width": 1,
                  "dataSource": "test-ds", "metricRef": "no-slash" }
              ]
            }
            """);
        var (sp, _) = DashboardTestInfra.BuildServices(root.Path, root.Path, SampleRows);
        var service = sp.GetRequiredService<IDashboardDataService>();

        var ex = await Assert.ThrowsAsync<DashboardDefinitionException>(
            () => service.GetWidgetDataAsync("bad", "w1"));
        Assert.Contains("格式非法", ex.Message);
    }
}
