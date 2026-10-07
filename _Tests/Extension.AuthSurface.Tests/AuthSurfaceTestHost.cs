using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthSurface.Tests;

/// <summary>
/// AuthSurface 测试基础设施——跨扩展消费方形态（AuthSurface 表 + 跨扩展基表模拟 + 两跨扩展视图）。
/// <para>⚠️ AuthCenter 主包源码处 V0.9.0 身份域重构进行中（半成品编译失败）——测试**不引 AuthCenter**；
/// vm 视图 JOIN 的跨扩展基表（<c>TKWF_AuthAccount</c> V0.9.0 凭据白名单列 / <c>AuthGrant</c> 应用授权底座）
/// 由本宿主**原生 SQL 建表 + 插数**模拟（对齐 AuthCenter V0.9.0 表结构；V0.9.0 完成后切 ProjectReference + 双白名单）。</para>
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

    /// <summary>VEntity 只读（IEntityReadOnlyDAC 注册——视图查询路径）。</summary>
    private static readonly Type[] ViewTypes =
    [
        typeof(UserRedemptionHistoryView),
        typeof(UserAppsView),
    ];

    /// <summary>注册全部实体真实 FreeSqlEntityDAC——IEntityDAC（读写 DataService）+ IEntityReadOnlyDAC（VEntity 只读）。</summary>
    public static void RegisterDacs(IServiceCollection services)
    {
        foreach (var entityType in SurfaceEntityTypes)
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
        // AuthSurface 表（SyncStructure——FreeSql 托管）
        fsql.CodeFirst.SyncStructure<RedemptionCodeEntity>();
        fsql.CodeFirst.SyncStructure<AuthAppEntity>();
        // 跨扩展基表（原生 SQL——AuthCenter V0.9.0 表结构模拟；须先于视图）
        fsql.Ado.ExecuteNonQuery(
            @"CREATE TABLE IF NOT EXISTS ""TKWF_AuthAccount"" (
  ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
  ""UId"" TEXT NOT NULL UNIQUE,
  ""Phone"" TEXT NULL,
  ""PasswordHash"" TEXT NULL,
  ""FederationAnchorOpenId"" TEXT NULL,
  ""AuthLevel"" INTEGER NOT NULL DEFAULT 1,
  ""TokenVersion"" INTEGER NOT NULL DEFAULT 0,
  ""IsEnabled"" INTEGER NOT NULL DEFAULT 1,
  ""CreateTime"" TEXT NOT NULL,
  ""UpdateTime"" TEXT NOT NULL)");
        fsql.Ado.ExecuteNonQuery(
            @"CREATE TABLE IF NOT EXISTS ""AuthGrant"" (
  ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
  ""UserId"" TEXT NOT NULL,
  ""AppId"" TEXT NOT NULL,
  ""Scopes"" TEXT NOT NULL DEFAULT '',
  ""ValidUntil"" TEXT NULL,
  ""Source"" TEXT NOT NULL DEFAULT '',
  ""Status"" INTEGER NOT NULL DEFAULT 0,
  ""CreateTime"" TEXT NOT NULL,
  ""UpdateTime"" TEXT NOT NULL)");
        // 两跨扩展视图（SQLite 方言，来自 ViewSqlSQLite——不跑宿主 SyncViewsAsync，对齐 IdentityTestHost.SyncSchema 先例）
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""TKWFV_UserRedemptionHistory"" AS
SELECT rc.""Id"", rc.""RedeemedByUId"" AS ""UserId"", rc.""CodeMasked"", rc.""ProductName"", rc.""TargetAppId"",
       rc.""RedeemedAtUtc"", rc.""Status"", ac.""UId""
FROM ""RedemptionCode"" rc
INNER JOIN ""TKWF_AuthAccount"" ac ON rc.""RedeemedByUId"" = ac.""UId""");
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""TKWFV_UserApps"" AS
SELECT g.""Id"", g.""UserId"", g.""AppId"", a.""AppName"", g.""Scopes"", g.""ValidUntil"", g.""Source"", g.""Status"", g.""CreateTime"",
       ac.""UId"", ac.""AuthLevel""
FROM ""AuthGrant"" g
INNER JOIN ""TKWF_AuthAccount"" ac ON g.""UserId"" = ac.""UId""
LEFT JOIN ""AuthApp"" a ON g.""AppId"" = a.""AppId""");
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
        services.AddOptions<AuthSurfaceOptions>();

        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        RegisterDacs(services);

        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>创建测试 AuthAccount（跨扩展模拟——TKWF_AuthAccount 表 V0.9.0 凭据列，原生 SQL 插数）。</summary>
    public static async Task SeedAccountAsync(IFreeSql fsql, string uid, string? phone = null, int authLevel = 1)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var phoneSql = phone is null ? "NULL" : $"'{phone}'";
        await fsql.Ado.ExecuteNonQueryAsync(
            $@"INSERT INTO ""TKWF_AuthAccount"" (""UId"", ""Phone"", ""AuthLevel"", ""TokenVersion"", ""IsEnabled"", ""CreateTime"", ""UpdateTime"")
VALUES ('{uid}', {phoneSql}, {authLevel}, 0, 1, '{now}', '{now}')");
    }

    /// <summary>创建测试 AuthGrant（跨扩展模拟——"我的应用"数据底座；Source=login）。</summary>
    public static async Task SeedGrantAsync(IFreeSql fsql, string userId, string appId, int status = 0, DateTime? validUntil = null, string source = "login")
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var validSql = validUntil is null ? "NULL" : $"'{validUntil.Value.ToString("yyyy-MM-dd HH:mm:ss")}'";
        await fsql.Ado.ExecuteNonQueryAsync(
            $@"INSERT INTO ""AuthGrant"" (""UserId"", ""AppId"", ""Scopes"", ""ValidUntil"", ""Source"", ""Status"", ""CreateTime"", ""UpdateTime"")
VALUES ('{userId}', '{appId}', '', {validSql}, '{source}', {status}, '{now}', '{now}')");
    }
}
