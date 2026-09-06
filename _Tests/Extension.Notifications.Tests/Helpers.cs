using System.Data;
using System.Threading;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>同步三张通知表结构（Notification / UserNotification / NotificationSubscription）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<NotificationEntity>();
        fsql.CodeFirst.SyncStructure<UserNotificationEntity>();
        fsql.CodeFirst.SyncStructure<NotificationSubscriptionEntity>();
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
        configure?.Invoke(services);
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        return services.BuildServiceProvider();
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
