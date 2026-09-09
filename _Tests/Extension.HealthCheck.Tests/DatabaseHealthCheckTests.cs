using System.Net;
using System.Text.Json;
using System.ComponentModel.DataAnnotations.Schema;
using FreeSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.HealthCheck;

namespace TKWF.Ext.HealthCheck.Tests;

/// <summary>
/// V0.2.0：DB 连通性探针测试——`AddDatabaseHealthCheck&lt;TEntity&gt;` 注册 + /health 聚合。
/// <para>手写测试实体（Oracle P1-2 选项 B）：<see cref="IDomainEntity"/> 接口极简（long Id + 默认实现），
/// 免测试项目 SG1 Analyzer 接线。SQLite :memory: 建表/删表制造 Healthy/Unhealthy（P2-3）。</para>
/// </summary>
public class DatabaseHealthCheckTests
{
    /// <summary>手写测试实体——映射 HealthCheckProbe 表（探针表级探测目标）。BCL [Table] 兼容 FreeSql。</summary>
    [Table("HealthCheckProbe")]
    private sealed class TestProbeEntity : IDomainEntity
    {
        public long Id { get; set; }
    }

    private static async Task<WebApplication> StartHostAsync(
        Action<IServiceCollection> configureServices,
        string connectionString = "Data Source=:memory:")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // 临时端口，并行测试互不冲突
        configureServices(builder.Services);

        // FreeSql SQLite :memory: 接线——手动注册 Scoped IEntityReadOnlyDAC<TestProbeEntity>
        // （模拟消费方 UseFreeSqlEntityDAC 的解析路径；对齐其他扩展测试手动 new FreeSqlBuilder 模式）
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, connectionString)
            .Build();
        fsql.CodeFirst.SyncStructure<TestProbeEntity>();   // 显式建表（FreeSql CodeFirst 不自动建表）
        builder.Services.AddSingleton(fsql);
        builder.Services.AddScoped<IEntityReadOnlyDAC<TestProbeEntity>>(_ =>
            new FreeSqlEntityDAC<TestProbeEntity>(new UnitOfWorkManager(fsql)));

        var app = builder.Build();
        app.MapTkfwHealthChecks();
        await app.StartAsync();
        return app;
    }

    private static string GetBoundAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        return server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    private static HttpClient CreateClient(WebApplication app)
        => new() { BaseAddress = new Uri(GetBoundAddress(app)) };

    /// <summary>经 Scoped IEntityReadOnlyDAC&lt;TestProbeEntity&gt; 触发 FreeSql 初始化（连接池预热——:memory: 每连接独立库）。</summary>
    private static void SyncProbeTable(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dac = scope.ServiceProvider.GetRequiredService<IEntityReadOnlyDAC<TestProbeEntity>>();
        _ = dac.CountAsync(dac.Query).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task AddDatabaseHealthCheck_Healthy_WhenTableExists()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks()
                .AddDatabaseHealthCheck<TestProbeEntity>("db");
        });
        SyncProbeTable(app);
        using var client = CreateClient(app);

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"Healthy\"", body);
    }

    [Fact]
    public async Task AddDatabaseHealthCheck_Unhealthy_WhenTableDropped()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks()
                .AddDatabaseHealthCheck<TestProbeEntity>("db");
        });
        SyncProbeTable(app);

        // 删表 → 查询异常 → Unhealthy（P2-3：SQLite :memory: 关闭连接会破坏隔离，删表不影响其他测试）
        using (var scope = app.Services.CreateScope())
        {
            var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
            fsql.Ado.ExecuteNonQuery("DROP TABLE IF EXISTS HealthCheckProbe");
        }

        using var client = CreateClient(app);
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"Unhealthy\"", body);
    }

    [Fact]
    public async Task AddDatabaseHealthCheck_Detailed_ShowsProbeEntry()
    {
        await using var app = await StartHostAsync(services =>
        {
            services.AddTkfwHealthChecks(o => o.Detailed = true)
                .AddDatabaseHealthCheck<TestProbeEntity>("db");
        });
        SyncProbeTable(app);
        using var client = CreateClient(app);

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(body);
        var entries = doc.RootElement.GetProperty("entries");
        Assert.Equal("Healthy", entries.GetProperty("db").GetProperty("status").GetString());
    }

    [Fact]
    public void AddDatabaseHealthCheck_Registers_Probe_With_Default_Timeout()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTkfwHealthChecks()
            .AddDatabaseHealthCheck<TestProbeEntity>("db");

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        var registration = options.Registrations.Single(r => r.Name == "db");
        Assert.NotNull(registration);
        Assert.Equal(typeof(HealthCheckServiceOptions), options.GetType()); // 探针注册于 HealthCheckServiceOptions
        Assert.Equal(TimeSpan.FromSeconds(5), registration.Timeout);        // P1-3：默认超时 5s
    }

    [Fact]
    public void AddDatabaseHealthCheck_CustomTimeout_Is_Respected()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTkfwHealthChecks()
            .AddDatabaseHealthCheck<TestProbeEntity>("db", timeout: TimeSpan.FromSeconds(30));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registration = options.Registrations.Single(r => r.Name == "db");

        Assert.Equal(TimeSpan.FromSeconds(30), registration.Timeout);
    }
}
