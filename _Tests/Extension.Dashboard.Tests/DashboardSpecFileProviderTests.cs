using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TKWF.Ext.Dashboard;

namespace TKWF.Ext.Dashboard.Tests;

/// <summary>
/// DashboardSpecFileProvider 测试——Dashboard 定义加载（JSON 反序列化 + 契约校验）+ Metrics 规格加载（IConfiguration 直读）。
/// </summary>
public class DashboardSpecFileProviderTests
{
    [Fact]
    public void LoadDashboard_LoadsDefinition()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "overview", DashboardTestInfra.DashboardJson());
        var provider = CreateProvider(root.Path, root.Path);

        var definition = provider.LoadDashboard("overview");

        Assert.Equal("overview", definition.Name);
        Assert.Equal("经营总览", definition.Title);
        Assert.Equal(2, definition.Widgets!.Count);
        var metricWidget = definition.Widgets[0];
        Assert.Equal(DashboardWidgetType.NumberContainer, metricWidget.Type);
        Assert.Equal("merchant/payment-metrics:total-revenue", metricWidget.MetricRef);
        Assert.Equal("test-ds", metricWidget.DataSource);
    }

    [Fact]
    public void LoadDashboard_MissingFile_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        var provider = CreateProvider(root.Path, root.Path);

        var ex = Assert.Throws<DashboardDefinitionException>(() => provider.LoadDashboard("nope"));
        Assert.Contains("读取失败", ex.Message);
    }

    [Fact]
    public void LoadDashboard_InvalidJson_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "bad", "{ not-json !!");
        var provider = CreateProvider(root.Path, root.Path);

        Assert.Throws<DashboardDefinitionException>(() => provider.LoadDashboard("bad"));
    }

    [Fact]
    public void LoadDashboard_MissingWidgets_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "empty", """{ "name": "empty", "title": "空" }""");
        var provider = CreateProvider(root.Path, root.Path);

        var ex = Assert.Throws<DashboardDefinitionException>(() => provider.LoadDashboard("empty"));
        Assert.Contains("widgets", ex.Message);
    }

    [Fact]
    public void LoadDashboard_DuplicateWidget_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "dup", """
            {
              "name": "dup", "title": "重复",
              "widgets": [
                { "name": "w1", "type": "list", "row": 0, "order": 0, "width": 1, "dataSource": "test-ds" },
                { "name": "w1", "type": "list", "row": 0, "order": 1, "width": 1, "dataSource": "test-ds" }
              ]
            }
            """);
        var provider = CreateProvider(root.Path, root.Path);

        var ex = Assert.Throws<DashboardDefinitionException>(() => provider.LoadDashboard("dup"));
        Assert.Contains("重复", ex.Message);
    }

    [Fact]
    public void LoadDashboard_MissingDataSource_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "nodatasrc", """
            {
              "name": "nodatasrc", "title": "缺数据源",
              "widgets": [
                { "name": "w1", "type": "list", "row": 0, "order": 0, "width": 1 }
              ]
            }
            """);
        var provider = CreateProvider(root.Path, root.Path);

        var ex = Assert.Throws<DashboardDefinitionException>(() => provider.LoadDashboard("nodatasrc"));
        Assert.Contains("dataSource", ex.Message);
    }

    [Fact]
    public void LoadDashboard_GroupPath_Resolves()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteDashboard(root.Path, "overview", DashboardTestInfra.DashboardJson(), group: "operations");
        var provider = CreateProvider(root.Path, root.Path);

        var definition = provider.LoadDashboard("operations/overview");

        Assert.Equal("overview", definition.Name);
    }

    [Fact]
    public void LoadMetricsSpec_LoadsDefinitions()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteMetricsSpec(root.Path, "merchant", "payment-metrics", DashboardTestInfra.MetricsSpecJson());
        var provider = CreateProvider(root.Path, root.Path);

        var definitions = provider.LoadMetricsSpec("merchant", "payment-metrics");

        Assert.Equal(2, definitions.Count);
        Assert.Contains(definitions, d => d.Name == "total-revenue");
        Assert.Contains(definitions, d => d.Name == "daily-trend");
    }

    [Fact]
    public void LoadMetricsSpec_MissingFile_Throws()
    {
        using var root = DashboardTestInfra.CreateTempRoot();
        var provider = CreateProvider(root.Path, root.Path);

        Assert.Throws<MetricDefinitionException>(() => provider.LoadMetricsSpec("merchant", "nope"));
    }

    [Fact]
    public void LoadMetricsSpec_ReadsMetricsSpecRoot_FromConfiguration()
    {
        // 验证 Oracle C1：Metrics SpecRoot 经 IConfiguration 直读 TKWF:Metrics:SpecRoot（Dashboard 不引 Metrics 扩展）
        using var dashboardRoot = DashboardTestInfra.CreateTempRoot();
        using var metricsRoot = DashboardTestInfra.CreateTempRoot();
        DashboardTestInfra.WriteMetricsSpec(metricsRoot.Path, "merchant", "payment-metrics", DashboardTestInfra.MetricsSpecJson());
        var provider = CreateProvider(dashboardRoot.Path, metricsRoot.Path);

        var definitions = provider.LoadMetricsSpec("merchant", "payment-metrics");

        Assert.Equal(2, definitions.Count);
    }

    private static DashboardSpecFileProvider CreateProvider(string dashboardSpecRoot, string metricsSpecRoot)
    {
        var options = new DashboardOptions { SpecRoot = dashboardSpecRoot };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TKWF:Metrics:SpecRoot"] = metricsSpecRoot
            })
            .Build();
        return new DashboardSpecFileProvider(Options.Create(options), configuration);
    }
}
