using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// AuditLogging 生产路径集成测试（V4.10.53 领域自治根治，ADR90，正确路线）——经<b>真实 DI</b>
/// （Initializer ConfigureServices + FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c>
/// + <c>User.Use&lt;接口&gt;()</c>（AOP 路径：设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂
/// → 经 ActivatorUtilities 直建实现）→ 断言查询/聚合读写落库。
/// <para>对齐 Settings V0.3.0 / Account V0.5.0 生产路径宿主范式；接线型 Store 经普通 DI GetService 解析
/// （主框架 AuditLogFilter 的解析链——修复哨兵：旧 ctor 注入 IDomainUser 致 GetService 构造失败、审计落库静默失效）。</para>
/// </summary>
public class AuditLogProductionPathTests
{
    private static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        return fsql;
    }

    /// <summary>
    /// 构建消费方生产形态 DI：Initializer ConfigureServices + FreeSql 基础设施。
    /// <para>DataService 双路径：<c>Use&lt;具体类&gt;()</c> NoAop 直建（IEntityDAC 从 DI 解析，红线合规）——
    /// 生产经 SG 消费方聚合 throw-factory 注册（仅满足 ValidateOnBuild）；<b>接线型 Store 的 C1 延迟解析源</b>则需
    /// DataService 普通 DI <b>可构造</b>（wiring 边界测试镜像——对齐 ProductionDataServiceResolutionTests 的
    /// AddConstructibleDataService 形态，非 throw-factory）。</para>
    /// </summary>
    private static ServiceProvider CreateProvider(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new AuditLoggingExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // FreeSql 基础设施（消费方 DomainHost 等价注册）
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<AuditLogEntity>, FreeSqlEntityDAC<AuditLogEntity>>();

        // 接线型 Store 的 DataService 解析源（普通 DI 可构造——wiring 边界测试镜像；DataService ctor 需 IDomainUser + IEntityDAC；
        // ⚠️ 测试内部桩非生产注册——生产 DataService 经 User.Use<具体类>() NoAop 直建，IDomainUser 永不注册 DI（D01）。
        // T3 候选：DataService 可构造工厂形态待框架统一。）
        services.AddScoped<IDomainUser>(_ => new StubDomainUser());
        services.AddScoped<AuditLogEntityDataService>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Use_IAuditLogQueryService_ThroughGuardFactory()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        // 生产路径：User.Use<IAuditLogQueryService>()——AOP 路径（设 CurrentAopUser → 守卫工厂 → 直建实现）
        var query = user.Use<IAuditLogQueryService>();
        Assert.NotNull(query);

        // 经 DataService NoAop 直建插入数据（审计 CRUD 语义保留）
        var ds = user.Use<AuditLogEntityDataService>();
        for (int i = 0; i < 3; i++)
        {
            await ds.EntityCreateAsync(new AuditLogEntity
            {
                UserName = "alice",
                UserId = "u1",
                ServiceName = "OrderService",
                MethodName = $"CreateOrder{i}",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-10),
                DurationMs = 50,
                Success = true,
                CorrelationId = $"corr-{i}",
                CreateTime = DateTimeOffset.Now
            }, CancellationToken.None);
        }

        var result = await query.GetListAsync(new AuditLogQueryInput { ServiceName = "OrderService" }, CancellationToken.None);
        Assert.Equal(3, result.Total);
        Assert.All(result.Items, item => Assert.Equal("OrderService", item.ServiceName));

        Assert.Equal(3, await query.CountAsync(new AuditLogQueryInput { UserName = "alice" }, CancellationToken.None));
    }

    [Fact]
    public async Task Use_IAuditLogAnalyticsService_ThroughGuardFactory()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        var analytics = user.Use<IAuditLogAnalyticsService>();
        Assert.NotNull(analytics);

        var ds = user.Use<AuditLogEntityDataService>();
        await ds.EntityCreateAsync(new AuditLogEntity
        {
            UserName = "alice", UserId = "u1", ServiceName = "OrderService", MethodName = "CreateOrder",
            ExecutionTime = DateTime.UtcNow.AddMinutes(-10), DurationMs = 50, Success = true, CorrelationId = "c1",
            CreateTime = DateTimeOffset.Now
        }, CancellationToken.None);
        await ds.EntityCreateAsync(new AuditLogEntity
        {
            UserName = "bob", UserId = "u2", ServiceName = "OrderService", MethodName = "DeleteOrder",
            ExecutionTime = DateTime.UtcNow.AddMinutes(-10), DurationMs = 30, Success = false, CorrelationId = "c2",
            CreateTime = DateTimeOffset.Now
        }, CancellationToken.None);

        var top = await analytics.GetTopServicesAsync(topN: 10, ct: CancellationToken.None);
        var order = Assert.Single(top);
        Assert.Equal("OrderService", order.Dimension);
        Assert.Equal(2, order.Count);

        var stats = await analytics.GetStatsAsync(ct: CancellationToken.None);
        Assert.Equal(2, stats.Total);
        Assert.Equal(1, stats.Succeeded);
        Assert.Equal(1, stats.Failed);
    }

    /// <summary>接线型生产路径哨兵：Store 经普通 DI 解析（主框架 AuditLogFilter 的 GetService 解析链）且落库生效——
    /// 修复真实生产故障：旧 ctor 注入 IDomainUser（永不注册 DI）致 GetService 构造失败、审计落库静默失效。</summary>
    [Fact]
    public async Task Store_From_PlainDi_SavesAuditEntry()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);

        // 主框架 AuditLogFilterAttribute 解析链：context.ServiceProvider.GetService<IAuditLogStore>()（普通 DI，非 Use<T>）
        var store = sp.GetService<IAuditLogStore>();
        Assert.NotNull(store);
        Assert.IsType<AuditLogStore>(store);

        await store!.SaveAsync(new AuditLogEntry(
            UserName: "testuser",
            UserId: "u123",
            ServiceName: "OrderService",
            MethodName: "CreateOrder",
            ArgumentsJson: "{\"id\":1}",
            ExecutionTime: DateTimeOffset.Now,
            DurationMs: 42,
            Success: true,
            Exception: null,
            CorrelationId: "corr-prod-1"), CancellationToken.None);

        // 落库断言（经 DataService 业务方法回查——红线：断言不经裸 fsql.Select）
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        var saved = await user.Use<AuditLogEntityDataService>()
            .EntityGetAsync(e => e.CorrelationId == "corr-prod-1", CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("testuser", saved!.UserName);
        Assert.Equal("OrderService", saved.ServiceName);
        Assert.True(saved.Success);
    }
}
