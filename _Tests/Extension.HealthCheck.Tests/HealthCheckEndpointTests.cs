using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.HealthCheck;

namespace TKWF.Ext.HealthCheck.Tests;

/// <summary>
/// HealthCheck 端点集成测试——最小 host（WebApplication + Kestrel loopback 临时端口）验证
/// MapTkfwHealthChecks：聚合状态 / Detailed 组件级输出 / 探针异常静默 / 路径可配 / Enabled=false 不映射。
/// </summary>
public class HealthCheckEndpointTests
{
    private static async Task<WebApplication> StartHostAsync(Action<IServiceCollection> configureServices)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // 临时端口，并行测试互不冲突
        configureServices(builder.Services);

        var app = builder.Build();
        app.MapTkfwHealthChecks();
        await app.StartAsync();
        return app;
    }

    /// <summary>经 IServerAddressesFeature 取实际绑定地址（UseUrls 端口 0 → OS 分配）。</summary>
    private static string GetBoundAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()!.Addresses;
        return addresses.First();
    }

    private static HttpClient CreateClient(WebApplication app)
        => new() { BaseAddress = new Uri(GetBoundAddress(app)) };

    /// <summary>GET 并断言状态码 + 返回响应体。
    /// 注：非 Healthy 状态默认 503（ASP.NET Core 内置 ResultStatusCodes——探针语义，LB/编排依据状态码判定）。</summary>
    private static async Task<string> GetBodyAsync(HttpClient client, string path, HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(expectedStatus, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MapTkfwHealthChecks_Healthy_Returns_200_And_StatusOnly()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks();
            services.AddHealthChecks().AddCheck("ok", () => HealthCheckResult.Healthy());
        });
        using var client = CreateClient(app);

        var body = await GetBodyAsync(client, "/health");

        Assert.Contains("\"Healthy\"", body);
        // Detailed=false（默认）：仅 status，不泄露组件细节
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
        Assert.False(doc.RootElement.TryGetProperty("entries", out _));
    }

    [Fact]
    public async Task MapTkfwHealthChecks_Unhealthy_Aggregates_OverallStatus()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks();
            services.AddHealthChecks()
                .AddCheck("ok", () => HealthCheckResult.Healthy())
                .AddCheck("bad", () => HealthCheckResult.Unhealthy("db down"));
        });
        using var client = CreateClient(app);

        // 非 Healthy → 503（探针语义）+ 响应体标注最差状态
        var body = await GetBodyAsync(client, "/health", HttpStatusCode.ServiceUnavailable);
        Assert.Contains("\"Unhealthy\"", body); // 最差状态胜出
    }

    [Fact]
    public async Task MapTkfwHealthChecks_Degraded_WorstWins()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks();
            services.AddHealthChecks()
                .AddCheck("ok", () => HealthCheckResult.Healthy())
                .AddCheck("slow", () => HealthCheckResult.Degraded("slow response"));
        });
        using var client = CreateClient(app);

        // Healthy + Degraded → Degraded（默认 ResultStatusCodes：Degraded → 200，仅 Unhealthy → 503）
        var body = await GetBodyAsync(client, "/health");
        Assert.Contains("\"Degraded\"", body);
    }

    [Fact]
    public async Task MapTkfwHealthChecks_Detailed_Outputs_ComponentLevelJson()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks(o => o.Detailed = true);
            services.AddHealthChecks()
                .AddCheck("db", () => HealthCheckResult.Healthy())
                .AddCheck("redis", () => HealthCheckResult.Unhealthy("redis down"));
        });
        using var client = CreateClient(app);

        var body = await GetBodyAsync(client, "/health", HttpStatusCode.ServiceUnavailable);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", doc.RootElement.GetProperty("status").GetString());
        var entries = doc.RootElement.GetProperty("entries");
        Assert.Equal("Healthy", entries.GetProperty("db").GetProperty("status").GetString());
        Assert.Equal("Unhealthy", entries.GetProperty("redis").GetProperty("status").GetString());
        Assert.Equal("redis down", entries.GetProperty("redis").GetProperty("description").GetString());
    }

    [Fact]
    public async Task MapTkfwHealthChecks_ProbeException_Returns_Unhealthy_NoThrow()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks();
            services.AddHealthChecks()
                .AddCheck("boom", () => throw new InvalidOperationException("probe crashed"));
        });
        using var client = CreateClient(app);

        // 探针异常 → 该项 Unhealthy（HealthCheckService 捕获）→ 503 不抛 500、不崩溃
        var body = await GetBodyAsync(client, "/health", HttpStatusCode.ServiceUnavailable);
        Assert.Contains("\"Unhealthy\"", body);
    }

    [Fact]
    public async Task MapTkfwHealthChecks_CustomPath_Is_Respected()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks(o => o.Path = "/hc");
            services.AddHealthChecks().AddCheck("ok", () => HealthCheckResult.Healthy());
        });
        using var client = CreateClient(app);

        var body = await GetBodyAsync(client, "/hc");
        Assert.Contains("\"Healthy\"", body);

        // 原 /health 不再映射
        var notFound = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task MapTkfwHealthChecks_Disabled_DoesNotMapEndpoint()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks(o => o.Enabled = false);
            services.AddHealthChecks().AddCheck("ok", () => HealthCheckResult.Healthy());
        });
        using var client = CreateClient(app);

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
