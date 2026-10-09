using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthSurface.Tests;

/// <summary>
/// AuthSurface 测试基础设施——跨扩展消费方形态（AuthSurface + AuthCenter 双扩展集成）。
/// <para>✅ AuthCenter V0.9.0 已提交（5bd2f0c——编译通过）——切 ProjectReference + 双白名单（Oracle4 P1-2 契约兑现）：
/// 跨扩展基表（<c>TKWF_AuthAccount</c>/<c>TKWF_AuthGrant</c>——ADR100 表名别名）由 AuthCenter 实体 SyncStructure 建（替代此前原生 SQL 模拟）。</para>
/// <para>生产路径：真实 FreeSqlEntityDAC + Provider（Initializer ConfigureServices + FreeSql + DAC）→ BindScope + user.Use&lt;门面&gt;() AOP。</para>
/// </summary>
internal static class AuthSurfaceTestHost
{
    /// <summary>AuthSurface 实体（IEntityDAC 读写注册）。</summary>
    private static readonly Type[] SurfaceEntityTypes =
    [
        typeof(RedemptionCodeEntity),
        typeof(AuthAppEntity),
    ];

    /// <summary>AuthCenter 跨扩展实体（vm 视图 JOIN 基表 + 测试造数——身份锚 / 应用授权底座）。</summary>
    private static readonly Type[] AuthCenterEntityTypes =
    [
        typeof(AuthAccountEntity),
        typeof(AuthGrantEntity),
    ];

    /// <summary>VEntity 只读（IEntityReadOnlyDAC 注册——视图查询路径）。</summary>
    private static readonly Type[] ViewTypes =
    [
        typeof(UserRedemptionHistoryView),
        typeof(UserAppsView),
    ];

    /// <summary>注册全部实体真实 FreeSqlEntityDAC——IEntityDAC（读写 DataService）+ IEntityReadOnlyDAC（VEntity 只读）。</summary>
    public static void RegisterDacs(IServiceCollection services)
    {
        foreach (var entityType in SurfaceEntityTypes.Concat(AuthCenterEntityTypes))
        {
            var dacType = typeof(IEntityDAC<>).MakeGenericType(entityType);
            var implType = typeof(FreeSqlEntityDAC<>).MakeGenericType(entityType);
            services.AddSingleton(dacType, implType);
        }
        foreach (var viewType in ViewTypes)
        {
            var dacType = typeof(IEntityReadOnlyDAC<>).MakeGenericType(viewType);
            var implType = typeof(FreeSqlEntityDAC<>).MakeGenericType(viewType);
            services.AddSingleton(dacType, implType);
        }
    }

    /// <summary>建表 + 建视图（Tier 1.5 SQLite 真实视图——Oracle4 P1-2 契约：基表先于视图）。</summary>
    public static void SyncSchema(IFreeSql fsql)
    {
        // AuthSurface 表（先）
        fsql.CodeFirst.SyncStructure<RedemptionCodeEntity>();
        fsql.CodeFirst.SyncStructure<AuthAppEntity>();
        // AuthCenter 表（跨扩展——vm 视图 JOIN 基表；V0.9.0 表名 TKWF_AuthAccount/TKWF_AuthGrant，ADR100）
        fsql.CodeFirst.SyncStructure<AuthAccountEntity>();
        fsql.CodeFirst.SyncStructure<AuthGrantEntity>();
        // 两跨扩展视图（SQLite 方言，来自 ViewSqlSQLite——不跑宿主 SyncViewsAsync，对齐 IdentityTestHost.SyncSchema 先例）
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""TKWFV_UserRedemptionHistory"" AS
SELECT rc.""Id"", rc.""RedeemedByUId"" AS ""UserId"", rc.""CodeMasked"", rc.""ProductName"", rc.""TargetAppId"",
       rc.""RedeemedAtUtc"", rc.""Status"", ac.""UId""
FROM ""TKWF_RedemptionCode"" rc
INNER JOIN ""TKWF_AuthAccount"" ac ON rc.""RedeemedByUId"" = ac.""UId""");
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""TKWFV_UserApps"" AS
SELECT g.""Id"", g.""UserId"", g.""AppId"", a.""AppName"", g.""Scopes"", g.""ValidUntil"", g.""Source"", g.""Status"", g.""CreateTime"",
       ac.""UId"", ac.""AuthLevel""
FROM ""TKWF_AuthGrant"" g
INNER JOIN ""TKWF_AuthAccount"" ac ON g.""UserId"" = ac.""UId""
LEFT JOIN ""TKWF_AuthApp"" a ON g.""AppId"" = a.""AppId""");
    }

    /// <summary>创建使用 SQLite 内存库的 IFreeSql + 建表建视图（每次调用新连接 = 独立内存库）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        SyncSchema(fsql);
        return fsql;
    }

    /// <summary>
    /// 构建消费方生产形态 DI：AuthSurface Initializer ConfigureServices + FreeSql 基础设施 + 全实体 DAC 注册。
    /// <paramref name="configure"/> 在基础设施之后执行（消费方 OnRegisterDomainServices 扩展点）。
    /// </summary>
    public static ServiceProvider CreateProvider(IFreeSql fsql, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new AuthSurfaceExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddOptions<AuthSurfaceOptions>().Configure(o =>
        {
            // 测试宿主 dev 模式——FileSymmetricKeyProvider 未配置密钥路径时随机 32 字节兜底（生产 fail-fast 语义由生产路径测试锁定）
            o.IsProduction = false;
        });

        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        RegisterDacs(services);

        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>创建测试 AuthAccount（跨扩展——TKWF_AuthAccount 表 V0.9.0 凭据列，FreeSql 直插）。</summary>
    public static async Task SeedAccountAsync(IFreeSql fsql, string uid, string? phone = null, int authLevel = 1)
        => await fsql.Insert(new AuthAccountEntity
        {
            UId = uid,
            Phone = phone,
            AuthLevel = authLevel,
            TokenVersion = 0,
            IsEnabled = true,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteAffrowsAsync();

    /// <summary>创建测试 AuthGrant（跨扩展——TKWF_AuthGrant 表，"我的应用"数据底座；Source=login）。</summary>
    public static async Task SeedGrantAsync(IFreeSql fsql, string userId, string appId, int status = 0, DateTime? validUntil = null, string source = "login")
        => await fsql.Insert(new AuthGrantEntity
        {
            UserId = userId,
            AppId = appId,
            Scopes = "",
            ValidUntil = validUntil,
            Source = source,
            Status = status,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }).ExecuteAffrowsAsync();
}
