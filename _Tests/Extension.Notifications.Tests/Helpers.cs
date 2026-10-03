using System.Data;
using System.Threading;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Notifications;
using TKWF.Ext.Permissions.Abstractions;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// Notifications 测试公共基础设施——SQLite 内存库 + 测试 DI 宿主构建器 + 默认通知定义 Provider + 权限 mock。
/// <para>V4.10.53（领域自治根治）宿主重写——走生产路径（对齐 MFA/Settings/Account 生产路径宿主）：
/// 真实 DI（扩展 ConfigureServices + FreeSql 基础设施 + AddLogging + <c>IEntityDAC&lt;T&gt;</c> Singleton）
/// → <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> → 门面经 <c>user.Use&lt;接口&gt;()</c> AOP 路径解析
/// （设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂 → ActivatorUtilities 直建）；
/// DataService 经基类 <c>User.Use&lt;XxxDataService&gt;()</c> NoAop 直建（IEntityDAC 从 DI 解析）。</para>
/// <para>⚠️ 边界保留组（本批不改，对齐 MFA IMfaMethod 处理）：<see cref="InboxNotifier"/> 仍构造注入 IDomainUser
/// （多实现集合 TryAddEnumerable——普通 DI 解析 ctor(IDomainUser) 时 IDomainUser 永不注册 → 生产解析失败，框架缺口候选 T3）；
/// 测试宿主为其注册可配置 <see cref="StubDomainUser"/>（IDomainUser 单例——门面经守卫工厂走真实 DomainUser，
/// 桩仅供未整改的 InboxNotifier / V0.5.0 <see cref="UserNotificationViewQueryService"/> 测试经 DI 解析）。
/// 其 <c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价（<c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>）。</para>
/// </summary>
internal sealed class NotificationTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly DomainUser<TestUserInfo> _user;

    /// <summary>根 ServiceProvider（供直解非门面服务——EmailNotifier/QueryService/事件 handler 等）。</summary>
    public ServiceProvider Provider => _serviceProvider;

    /// <summary>发布器门面（生产 AOP 路径：User.Use&lt;INotificationPublisher&gt;() 经守卫工厂创建实例）。</summary>
    public INotificationPublisher Publisher => _user.Use<INotificationPublisher>();

    /// <summary>真实绑定用户（V4.10.53——事件 handler 等消费方示例经此设 AOP 帧解析守卫门面）。</summary>
    public DomainUser<TestUserInfo> User => _user;

    /// <summary>收件箱门面（生产 AOP 路径）。</summary>
    public INotificationStore Store => _user.Use<INotificationStore>();

    /// <summary>订阅管理门面（生产 AOP 路径）。</summary>
    public INotificationSubscriptionManager SubscriptionManager => _user.Use<INotificationSubscriptionManager>();

    /// <summary>偏好管理门面（生产 AOP 路径）。</summary>
    public INotificationPreferenceManager PreferenceManager => _user.Use<INotificationPreferenceManager>();

    /// <summary>定义管理器（接线型 Singleton——普通 DI 解析）。</summary>
    public INotificationDefinitionManager DefinitionManager => _serviceProvider.GetRequiredService<INotificationDefinitionManager>();

    private NotificationTestHost(ServiceProvider serviceProvider, DomainUser<TestUserInfo> user)
    {
        _serviceProvider = serviceProvider;
        _user = user;
    }

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
    /// 构建测试 DI 宿主（生产路径，V4.10.53 领域自治根治）：
    /// 注册日志 + 内存 fsql + IEntityDAC 基础设施（Singleton + 共享 UnitOfWorkManager——消费方 DomainHost 等价注册）
    /// + 默认通知定义 Provider + <see cref="NotificationsExtensionInitializer{TUserInfo}.ConfigureServices"/>（扩展默认实现注册），
    /// 然后 <c>DomainUser&lt;TestUserInfo&gt;.BindScope</c> 绑定解析作用域 + 创建真实 DomainUser。
    /// <para><paramref name="configure"/> 可在初始化器之前追加额外注册（如权限 mock、额外定义 Provider、事务管理器覆盖）。</para>
    /// </summary>
    public static NotificationTestHost Build(IFreeSql fsql, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);
        // IEntityDAC<T> 基础设施注册（消费方 DomainHost 等价注册——Singleton + 共享 UnitOfWorkManager，
        // 对齐 Settings/Account/MFA 生产路径宿主）
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<NotificationEntity>, FreeSqlEntityDAC<NotificationEntity>>();
        services.AddSingleton<IEntityDAC<UserNotificationEntity>, FreeSqlEntityDAC<UserNotificationEntity>>();
        services.AddSingleton<IEntityDAC<NotificationSubscriptionEntity>, FreeSqlEntityDAC<NotificationSubscriptionEntity>>();
        services.AddSingleton<IEntityDAC<NotificationPreferenceEntity>, FreeSqlEntityDAC<NotificationPreferenceEntity>>();
        // V0.2.0 VEntity：IEntityReadOnlyDAC 只读契约（手写只读 DataService 经基类 User.Use 直建时从 DI 解析）
        services.AddSingleton<IEntityReadOnlyDAC<UserNotificationView>, FreeSqlEntityDAC<UserNotificationView>>();
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();
        services.AddSingleton<INotificationDefinitionProvider, TestNotificationDefinitions>();

        // ⚠️ 边界保留组（本批不改）：InboxNotifier 仍 ctor 注入 IDomainUser——多实现集合普通 DI 解析需此桩
        //（门面经守卫工厂走真实 DomainUser，此桩仅供未整改的 InboxNotifier / QueryService 测试经 DI 解析——T3 候选记录）
        var stubUser = new StubDomainUser();
        services.AddSingleton<IDomainUser>(sp => { stubUser.ServiceProvider = sp; return stubUser; });

        // V0.5.0：UserNotificationViewQueryService（[GenerateController] Service 包装类）——
        // 镜像生产 SG1b 自动注册（MetaType.Service → AddService）的可构造测试版（直接 GetRequiredService 解析），
        // 依赖 VEntity DataService + IDomainUser（经 DI 桩解析——仅本人测试经 configure 覆盖为 AuthenticatedStubUser）
        services.AddScoped<UserNotificationViewQueryService>(sp =>
            (UserNotificationViewQueryService)ActivatorUtilities.CreateInstance(
                sp, typeof(UserNotificationViewQueryService), sp.GetRequiredService<IDomainUser>()));

        configure?.Invoke(services);
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        // 生产路径：绑定解析作用域 + 真实 DomainUser（AOP 路径 CurrentAopUser 来源）
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("notification-test-user", "通知测试用户") };

        return new NotificationTestHost(provider, user);
    }

    /// <summary>解析服务（Scoped 服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    /// <summary>释放宿主。</summary>
    public void Dispose() => _serviceProvider.Dispose();

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

/// <summary>
/// 测试用户桩——实现 IDomainUser 最小契约（默认匿名，无租户）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价实现——具体类经
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c> 直建（IDomainUser 参数显式传 this，
/// IEntityDAC 等其余从 DI 解析——对齐 Settings/Tagging/MFA 测试桩）。</para>
/// <para>⚠️ 本桩供未整改的 <see cref="InboxNotifier"/>（多实现集合成员仍 ctor 注入 IDomainUser——T3 候选）
/// 与 <see cref="UserNotificationViewQueryService"/>（V0.5.0 测试直接解析）经 DI 解析；
/// 4 标准门面经真实 <see cref="DomainUser{TUserInfo}"/> + 守卫工厂 AOP 路径解析（不触此桩）。</para>
/// </summary>
internal class StubDomainUser : IDomainUser
{
    private IServiceProvider? _provider;

    /// <summary>ServiceProvider（宿主注册工厂注入——懒加载 Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider? ServiceProvider
    {
        set { _provider = value; }
        get => _provider;
    }

    public string SessionKey => "test-session";
    public bool IsAuthenticated => false;
    public bool IsSystemActor => false;
    public IUserInfo? UserInfo => null;
    public long? TenantId => null;
    public bool IsNoAuditActive => false;
    public virtual string? UserId => null;
    public string? UserName => null;
    public bool IsInRole(string role) => false;

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        if (_provider is null)
            throw new InvalidOperationException("Stub: ServiceProvider 未注入——Use<T> 解析不可用");
        // 生产 NoAop 路径等价：ActivatorUtilities 直建，IDomainUser 参数显式传 this，其余从 DI 解析
        return (TDomainService)ActivatorUtilities.CreateInstance(_provider, typeof(TDomainService), this);
    }

    public TService GetService<TService>() where TService : notnull
    {
        if (_provider is null)
            throw new InvalidOperationException("Stub: ServiceProvider 未注入——GetService<T> 解析不可用");
        return _provider.GetRequiredService<TService>();
    }

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}

/// <summary>认证用户桩——具 userId 的 IDomainUser（V0.5.0 仅本人测试用）。</summary>
internal sealed class AuthenticatedStubUser(string userId) : StubDomainUser
{
    public override string? UserId => userId;
}
