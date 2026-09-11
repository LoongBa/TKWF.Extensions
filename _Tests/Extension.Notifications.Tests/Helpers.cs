using System.Data;
using System.Threading;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Notifications;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// Notifications 测试公共基础设施——SQLite 内存库 + 测试 DI 宿主构建器 + 默认通知定义 Provider + 权限 mock。
/// <para>对齐 DataPort（DataImportTaskServiceTests）与 AuditLogging（FreeSqlAuditLogStoreTests）测试模式：
/// 每测试类独立 fsql（using 释放）；经 DI 容器解析扩展服务（Publisher/Store/SubscriptionManager/DefinitionManager）。
/// 通知定义 Provider 以 DI 注册方式接入（<c>INotificationDefinitionProvider</c>），由初始化的
/// <see cref="INotificationDefinitionManager"/> 收集——发布/订阅/收件箱测试共用同一组注册定义。</para>
/// </summary>
internal static class NotificationTestHost
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（每次调用新连接 = 独立内存库）。
    /// 注意：连接需在测试生命周期内保持打开（using），关闭即清空数据库。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>同步三张通知表结构（Notification / UserNotification / NotificationSubscription）+ VEntity 视图。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<NotificationEntity>();
        fsql.CodeFirst.SyncStructure<UserNotificationEntity>();
        fsql.CodeFirst.SyncStructure<NotificationSubscriptionEntity>();
        fsql.CodeFirst.SyncStructure<NotificationPreferenceEntity>();   // V0.3.0：偏好表
        // V0.2.0 VEntity：建真实视图（SQLite 方言，来自 UserNotificationView.ViewSqlSQLite）——不跑宿主 SyncViewsAsync
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""vw_UserNotificationView"" AS
SELECT un.""Id"", un.""UserId"", un.""NotificationId"", un.""State"", un.""ReadTime"", un.""CreateTime"",
       n.""Name"", n.""Severity"", n.""DisplayName""
FROM ""UserNotification"" un
INNER JOIN ""Notification"" n ON un.""NotificationId"" = n.""Id""");
    }

    /// <summary>
    /// 构建测试 DI 宿主：注册日志 + 内存 fsql + 默认通知定义 Provider，然后执行
    /// <see cref="NotificationsExtensionInitializer{TUserInfo}.ConfigureServices"/>（扩展默认实现注册）。
    /// <para><paramref name="configure"/> 可在初始化器之前追加额外注册（如权限 mock、额外定义 Provider）。</para>
    /// </summary>
    public static ServiceProvider Build(IFreeSql fsql, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);
        services.AddSingleton<ITransactionManager>(new NoopTransactionManager());
        services.AddSingleton<INotificationDefinitionProvider, TestNotificationDefinitions>();
        // v4.10.8 (ADR61) 迁移：模拟生产 DataService 自动注册——测试容器不走消费方 SG 聚合，
        // 用与生产同构的 DI 兜底工厂（镜像 AddConstructibleDataService：ActivatorUtilities.CreateInstance
        // + 域用户；测试用户源 = DI IDomainUser 而非 AsyncLocal CurrentAopUser——免域作用域，xUnit 并行安全）。
        services.AddScoped<UnitOfWorkManager>();
        services.AddScoped<IEntityDAC<NotificationEntity>>(sp => new FreeSqlEntityDAC<NotificationEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<UserNotificationEntity>>(sp => new FreeSqlEntityDAC<UserNotificationEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<NotificationSubscriptionEntity>>(sp => new FreeSqlEntityDAC<NotificationSubscriptionEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<NotificationPreferenceEntity>>(sp => new FreeSqlEntityDAC<NotificationPreferenceEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IDomainUser>(_ => new StubDomainUser());
        AddTestConstructibleDataService<NotificationEntityDataService>(services);
        AddTestConstructibleDataService<UserNotificationEntityDataService>(services);
        AddTestConstructibleDataService<NotificationSubscriptionEntityDataService>(services);
        AddTestConstructibleDataService<NotificationPreferenceEntityDataService>(services);   // V0.3.0：偏好 DataService
        // V0.2.0 VEntity：IEntityReadOnlyDAC 只读契约 + 手写只读 DataService（同路径 DI 兜底工厂）
        services.AddScoped<IEntityReadOnlyDAC<UserNotificationView>>(sp => new FreeSqlEntityDAC<UserNotificationView>(sp.GetRequiredService<UnitOfWorkManager>()));
        AddTestConstructibleDataService<UserNotificationViewDataService>(services);
        configure?.Invoke(services);
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>v4.10.8 (ADR61) 迁移：测试版可构造 DataService 工厂——镜像生产
    /// <c>AddConstructibleDataService</c>（<c>ActivatorUtilities.CreateInstance</c> + 域用户），
    /// 用户源改为 DI <c>IDomainUser</c>（StubDomainUser）而非 AsyncLocal <c>CurrentAopUser</c>——
    /// 免域作用域、xUnit 并行隔离安全（不设 AsyncLocal）。</summary>
    private static void AddTestConstructibleDataService<T>(IServiceCollection services)
        where T : class
    {
        services.AddScoped<T>(sp =>
        {
            var user = sp.GetRequiredService<IDomainUser>();
            return (T)ActivatorUtilities.CreateInstance(sp, typeof(T), user);
        });
    }

    /// <summary>断言时间列与 <paramref name="reference"/>（UTC）在 1 秒容忍范围内。
    /// <para>SQLite Provider 将 DateTime.UtcNow 存为本地墙上时间（无 Kind 标识），
    /// 读出为 Unspecified Kind——比较前统一转为 UTC（假定 actual 为本地时间）。</para></summary>
    public static void AssertRecent(DateTime actual, DateTime reference)
    {
        var actualUtc = actual.Kind == DateTimeKind.Utc
            ? actual
            : DateTime.SpecifyKind(actual, DateTimeKind.Local).ToUniversalTime();
        Assert.True(
            actualUtc >= reference.AddSeconds(-1) && actualUtc <= reference.AddSeconds(1),
            $"时间列超出容忍范围：实际 {actual:O}，参考 {reference:O}");
    }
}

/// <summary>
/// 空事务管理器桩——SQLite 内存库单条写入自带原子性，测试无需物理事务；
/// 满足 <see cref="NotificationPublisher"/> 的 ITransactionManager 构造依赖（C4 事务包裹在真实宿主生效）。
/// </summary>
internal sealed class NoopTransactionManager : ITransactionManager
{
    public ITransactionScope Begin(IsolationLevel isolationLevel = IsolationLevel.Serializable)
        => new NoopScope();

    public bool IsActive => false;

    private sealed class NoopScope : ITransactionScope
    {
        public bool IsActive => false;
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Commit() { }
        public void Rollback() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// 测试默认通知定义 Provider——注册 2 个业务通知（均无权限门控，供发布/订阅/收件箱/定义测试共用）。
/// <para>经 DI 注册（<c>AddSingleton&lt;INotificationDefinitionProvider&gt;</c>），
/// 由 <see cref="INotificationDefinitionManager"/> 在容器解析时收集。</para>
/// </summary>
internal sealed class TestNotificationDefinitions : INotificationDefinitionProvider
{
    public const string OrderShipped = "OrderShipped";
    public const string SystemAlert = "SystemAlert";

    public void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(OrderShipped, "订单已发货"));
        context.Add(new NotificationDefinition(SystemAlert, "系统公告", NotificationSeverity.Warning));
    }
}

/// <summary>测试权限 mock——按预置字典返回授予结果（未列入的权限默认拒绝）。</summary>
internal sealed class FakePermissionChecker : IPermissionChecker
{
    private readonly Dictionary<string, bool> _grants;

    public FakePermissionChecker(Dictionary<string, bool> grants) => _grants = grants;

    public Task<bool> IsGrantedAsync(string permissionName)
        => Task.FromResult(_grants.GetValueOrDefault(permissionName, false));

    public Task<Dictionary<string, bool>> IsGrantedAsync(params string[] permissionNames)
        => Task.FromResult(permissionNames.ToDictionary(n => n, n => _grants.GetValueOrDefault(n, false)));
}

/// <summary>测试批量权限 mock（V0.3.0 逐用户权限门控）——按预置"用户 ID 集合"返回授予结果（未列入默认拒绝）。</summary>
internal sealed class FakePermissionBatchChecker : IPermissionBatchChecker
{
    private readonly HashSet<long> _grantedUserIds;

    public FakePermissionBatchChecker(params long[] grantedUserIds) => _grantedUserIds = new HashSet<long>(grantedUserIds);

    public Task<Dictionary<long, bool>> IsGrantedAsync(IReadOnlyList<long> userIds, string permissionName)
        => Task.FromResult(userIds.Distinct().ToDictionary(id => id, id => _grantedUserIds.Contains(id)));
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。</summary>
internal sealed class StubDomainUser : IDomainUser
{
    public string SessionKey => "test-session";
    public bool IsAuthenticated => false;
    public bool IsSystemActor => false;
    public IUserInfo? UserInfo => null;
    public long? TenantId => null;
    public bool IsNoAuditActive => false;
    public string? UserId => null;
    public string? UserName => null;
    public bool IsInRole(string role) => false;

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
        => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");

    public TService GetService<TService>() where TService : notnull
        => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");

    public TService GetOptionalService<TService>() where TService : class => null!;
    public System.Collections.Generic.IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
