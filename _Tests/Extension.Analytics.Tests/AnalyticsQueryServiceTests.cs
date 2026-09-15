using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKWF.Ext.Analytics;

namespace TKWF.Ext.Analytics.Tests;

/// <summary>
/// AnalyticsQueryService 门面单测——组合 Loader/Validator、pivot-hint 读取、manifest 透传、DI 接线。
/// </summary>
public class AnalyticsQueryServiceTests
{
    private const string Domain = "merchant";
    private const string DailyTrend = "PaymentLogStatView--daily-sales-trend";

    private static string ResolveSpecRoot()
        => Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static AnalyticsQueryService CreateService()
        => new(new AnalyticsSpecLoader(ResolveSpecRoot()));

    // ---------- 门面组合 Loader/Validator ----------

    [Fact]
    public async Task GetSemanticTypes_ActiveSpec_ReturnsFlatMap()
    {
        var service = CreateService();
        using var doc = await service.GetSemanticTypesAsync(Domain, DailyTrend, CancellationToken.None);

        Assert.Equal("Date",
            doc.RootElement.GetProperty("semantic_types").GetProperty("BizDate").GetProperty("semanticType").GetString());
    }

    [Fact]
    public async Task GetChartSpec_Default_ReturnsPrimaryLine()
    {
        var service = CreateService();
        using var doc = await service.GetChartSpecAsync(Domain, DailyTrend, null, CancellationToken.None);

        Assert.Equal("Line Chart", doc.RootElement.GetProperty("chartType").GetString());
    }

    [Fact]
    public async Task GetChartSpec_UnknownChartType_FallsBackToPrimary()
    {
        var service = CreateService();
        using var doc = await service.GetChartSpecAsync(Domain, DailyTrend, "nonexistent", CancellationToken.None);

        Assert.Equal("Line Chart", doc.RootElement.GetProperty("chartType").GetString());
    }

    [Fact]
    public async Task GetManifest_ReturnsRawManifest_WithStatus()
    {
        var service = CreateService();
        using var doc = await service.GetManifestAsync(Domain, DailyTrend, CancellationToken.None);

        Assert.Equal("active", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(DailyTrend, doc.RootElement.GetProperty("specKey").GetString());
    }

    // ---------- 状态校验（stale 语义经门面透传） ----------

    [Fact]
    public async Task GetSemanticTypes_StaleSpec_ThrowsSpecStaleException()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<SpecStaleException>(() =>
            service.GetSemanticTypesAsync(Domain, "PaymentLogStatView--weekly-sales-trend", CancellationToken.None));

        Assert.Contains("stale", ex.Reason);
    }

    [Fact]
    public async Task GetManifest_StaleSpec_DoesNotThrow()
    {
        // P1-4：manifest 透传不校验 status——前端读 status 展示 stale
        var service = CreateService();
        using var doc = await service.GetManifestAsync(Domain, "PaymentLogStatView--weekly-sales-trend", CancellationToken.None);

        Assert.Equal("stale", doc.RootElement.GetProperty("status").GetString());
    }

    // ---------- pivot-hint ----------

    [Fact]
    public async Task ListPivotChartTypes_FileExists_ReturnsAllowedTransitions()
    {
        var service = CreateService();
        var transitions = await service.ListPivotChartTypesAsync(Domain, DailyTrend, CancellationToken.None);

        Assert.Equal(new[] { "bar", "area" }, transitions.ToArray());
    }

    [Fact]
    public async Task ListPivotChartTypes_FileMissing_ReturnsEmpty()
    {
        var service = CreateService();
        var transitions = await service.ListPivotChartTypesAsync(Domain, "StoreRankingView--top-stores", CancellationToken.None);

        Assert.Empty(transitions);
    }

    // ---------- 命名校验 / 路径安全 ----------

    [Fact]
    public async Task GetSemanticTypes_InvalidSpecKey_ThrowsChartSpecLoadException()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ChartSpecLoadException>(() =>
            service.GetSemanticTypesAsync(Domain, "../etc", CancellationToken.None));
    }

    [Fact]
    public async Task GetSemanticTypes_InvalidDomain_ThrowsChartSpecLoadException()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ChartSpecLoadException>(() =>
            service.GetSemanticTypesAsync("Merchant", DailyTrend, CancellationToken.None));
    }

    // ---------- DI 接线 ----------

    [Fact]
    public void Di_RegistersFacade()
    {
        var services = new ServiceCollection();
        services.AddOptions<AnalyticsOptions>();
        services.Configure<AnalyticsOptions>(o => o.SpecRoot = ResolveSpecRoot());
        services.AddSingleton(sp => new AnalyticsSpecLoader(sp.GetRequiredService<IOptions<AnalyticsOptions>>().Value));
        services.AddSingleton<IAnalyticsQueryService, AnalyticsQueryService>();

        using var provider = services.BuildServiceProvider();
        var facade = provider.GetRequiredService<IAnalyticsQueryService>();

        Assert.IsType<AnalyticsQueryService>(facade);
    }
}
