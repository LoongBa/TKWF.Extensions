using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TKW.Framework.Domain.Web.Extensions;
using TKWF.Ext.HealthCheck;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.HealthCheck.Tests;

/// <summary>
/// HealthCheck 端点集成测试——最小 host（WebApplication + Kestrel loopback 临时端口）验证
/// HealthCheckWebExtension.DeclareEndpoints → 框架注册表 MapInfrastructure(EndpointKind.HealthCheck)
/// （F13 收敛迁移 2026-10-24——ConfigureEndpoints 删除，端点映射上移框架注册表，路径/开关读 TKWF:HealthCheck 配置节）：
/// 聚合状态 / 探针异常静默 / 路径可配（配置节）/ Enabled=false 不映射。Detailed 组件级输出随方案 A 删除。
/// </summary>
public class HealthCheckEndpointTests
{
    private static async Task<WebApplication> StartHostAsync(
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<IHealthChecksBuilder>? configureChecks = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // 临时端口，并行测试互不冲突
        configureBuilder?.Invoke(builder);
        var extension = new HealthCheckWebExtension();
        extension.ConfigureServices(builder.Services);
        configureChecks?.Invoke(builder.Services.AddHealthChecks());

        var app = builder.Build();
        extension.DeclareEndpoints(new DefaultExtensionEndpointRegistry<TestUserInfo>(app));
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
    public async Task DeclareEndpoints_Healthy_Returns_200_And_StatusOnly()
    {
        await using var app = await StartHostAsync(
            configureChecks: b => b.AddCheck("ok", () => HealthCheckResult.Healthy()));
        using var client = CreateClient(app);

        var body = await GetBodyAsync(client, "/health");

        Assert.Contains("\"Healthy\"", body);
        // 摘要 writer（方案 A）：仅 status，不泄露组件细节
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
        Assert.False(doc.RootElement.TryGetProperty("entries", out _));
    }

    [Fact]
    public async Task DeclareEndpoints_Unhealthy_Aggregates_OverallStatus()
    {
        await using var app = await StartHostAsync(
            configureChecks: b => b
                .AddCheck("ok", () => HealthCheckResult.Healthy())
                .AddCheck("bad", () => HealthCheckResult.Unhealthy("db down")));
        using var client = CreateClient(app);

        // 非 Healthy → 503（探针语义）+ 响应体标注最差状态
        var body = await GetBodyAsync(client, "/health", HttpStatusCode.ServiceUnavailable);
        Assert.Contains("\"Unhealthy\"", body);
    }

    [Fact]
    public async Task DeclareEndpoints_Degraded_WorstWins()
    {
        await using var app = await StartHostAsync(
            configureChecks: b => b
                .AddCheck("ok", () => HealthCheckResult.Healthy())
                .AddCheck("slow", () => HealthCheckResult.Degraded("slow response")));
        using var client = CreateClient(app);

        // Healthy + Degraded → Degraded（默认 ResultStatusCodes：Degraded → 200，仅 Unhealthy → 503）
        var body = await GetBodyAsync(client, "/health");
        Assert.Contains("\"Degraded\"", body);
    }

    [Fact]
    public async Task DeclareEndpoints_ProbeException_Returns_Unhealthy_NoThrow()
    {
        await using var app = await StartHostAsync(
            configureChecks: b => b
                .AddCheck("boom", () => throw new InvalidOperationException("probe crashed")));
        using var client = CreateClient(app);

        // 探针异常 → 该项 Unhealthy（HealthCheckService 捕获）→ 503 不抛 500、不崩溃
        var body = await GetBodyAsync(client, "/health", HttpStatusCode.ServiceUnavailable);
        Assert.Contains("\"Unhealthy\"", body);
    }

    [Fact]
    public async Task DeclareEndpoints_CustomPath_From_ConfigSection_Is_Respected()
    {
        await using var app = await StartHostAsync(
            configureBuilder: b => b.Configuration["TKWF:HealthCheck:Path"] = "/hc",
            configureChecks: b => b.AddCheck("ok", () => HealthCheckResult.Healthy()));
        using var client = CreateClient(app);

        var body = await GetBodyAsync(client, "/hc");
        Assert.Contains("\"Healthy\"", body);

        // 原 /health 不再映射
        var notFound = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task DeclareEndpoints_Disabled_From_ConfigSection_DoesNotMapEndpoint()
    {
        await using var app = await StartHostAsync(
            configureBuilder: b => b.Configuration["TKWF:HealthCheck:Enabled"] = "false",
            configureChecks: b => b.AddCheck("ok", () => HealthCheckResult.Healthy()));
        using var client = CreateClient(app);

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}