using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TKWF.Ext.RateLimiting.Tests;

/// <summary>
/// 测试基础设施——最小 WebApplication + TestServer 起端点，HTTP 级验证限流中间件行为。
/// <para>模拟远程 IP / 已认证用户：pipeline 前置中间件从测试请求头
/// （X-Test-Ip / X-Test-User）写入 HttpContext.Features（IConnectionFeature）与 HttpContext.User
/// ——分区器按 HttpContext.User 与 RemoteIpAddress 解析（Oracle C1），验证真实中间件管线。</para>
/// </summary>
internal static class RateLimitingTestInfra
{
    public const string TestIpHeader = "X-Test-Ip";
    public const string TestUserHeader = "X-Test-User";

    /// <summary>
    /// 构建 TestServer 宿主：测试头模拟中间件 + AddTkfwRateLimiting + 端点 + UseRateLimiter。
    /// </summary>
    /// <param name="configure">AddTkfwRateLimiting 编程式配置。</param>
    /// <param name="configuration">附加 IConfiguration（Options 绑定测试用；合并进 WebApplication 配置源）。</param>
    /// <param name="mapEndpoints">自定义端点映射（默认 /api/test GET + /api/auth/login POST + /api/public GET）。</param>
    public static TestHostSuite BuildHost(
        Action<RateLimitingOptions>? configure = null,
        IConfiguration? configuration = null,
        Action<WebApplication>? mapEndpoints = null)
    {
        var builder = WebApplication.CreateBuilder();

        if (configuration != null)
            builder.Configuration.AddConfiguration(configuration);

        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddTkfwRateLimiting(configure);

        var app = builder.Build();

        // 测试头模拟中间件（限流中间件之前——分区器读取 User/RemoteIpAddress 已就绪）
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.TryGetValue(TestIpHeader, out var ipHeader) && ipHeader.Count > 0)
            {
                ctx.Features.Set<IHttpConnectionFeature>(
                    new HttpConnectionFeature { RemoteIpAddress = IPAddress.Parse(ipHeader.ToString()!) });
            }
            if (ctx.Request.Headers.TryGetValue(TestUserHeader, out var userHeader) && userHeader.Count > 0)
            {
                ctx.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userHeader.ToString()!) },
                        "test"));
            }
            await next(ctx);
        });

        app.UseRateLimiter();

        if (mapEndpoints != null)
        {
            mapEndpoints(app);
        }
        else
        {
            app.MapGet("/api/test", () => Results.Ok("ok"));
            app.MapPost("/api/auth/login", () => Results.Ok("login"));
            app.MapGet("/api/public", () => Results.Ok("public"));
        }

        // 端点注册完成后再启动宿主（TestServer 请求处理前必须）
        app.StartAsync().GetAwaiter().GetResult();

        return new TestHostSuite(app.GetTestServer(), app);
    }

    /// <summary>创建已带指定远程 IP / 用户头的 HttpClient。IP 为 null 时不加头（RemoteIpAddress 保持默认）。</summary>
    public static HttpClient CreateClient(TestServer server, IPAddress? remoteIp = null, string? userId = null)
    {
        var client = server.CreateClient();
        if (remoteIp != null)
            client.DefaultRequestHeaders.Add(TestIpHeader, remoteIp.ToString());
        if (userId != null)
            client.DefaultRequestHeaders.Add(TestUserHeader, userId);
        return client;
    }
}

/// <summary>测试常用 IP 常量。</summary>
internal static class TestIps
{
    public static readonly IPAddress A = IPAddress.Parse("192.168.1.10");
    public static readonly IPAddress B = IPAddress.Parse("192.168.1.20");
    public static readonly IPAddress C = IPAddress.Parse("192.168.1.30");
}

/// <summary>
/// 测试宿主套餐——同时管理 TestServer 与 WebApplication 的生命周期
/// （using var suite = TestInfra.BuildHost(...) 即可，释放 WebApplication 即连带释放主机）。
/// </summary>
public sealed class TestHostSuite : IDisposable
{
    public TestServer Server { get; }
    private readonly WebApplication _app;

    public TestHostSuite(TestServer server, WebApplication app)
    {
        Server = server;
        _app = app;
    }

    public void Dispose()
    {
        try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch (ObjectDisposedException) { }
        Server.Dispose();
    }
}