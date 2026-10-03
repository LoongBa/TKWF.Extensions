using System.Data;
using System.Threading;
using FreeSql;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Notifications;
using TKWF.Ext.Notifications.SignalR;

namespace TKWF.Ext.Notifications.SignalR.Tests;

/// <summary>
/// SignalR 集成测试公共基础设施——SQLite 内存库 + 测试 DI 宿主构建器（P2-4 独立 Host 副本）。
/// <para>真实 <see cref="NotificationPublisher"/>（主包）+ Fake <see cref="IHubContext{NotificationsHub}"/>
/// （SignalR 包）跨包协作；对齐主包测试 <c>NotificationTestHost</c> 模式——不依赖主包测试 internal Helper
/// （主包 csproj <c>InternalsVisibleTo</c> 不含 SignalR 测试项目）。</para>
/// <para>V4.10.53（领域自治根治，ADR90）宿主重写——走生产路径（对齐主包 <c>NotificationTestHost</c> 形态，
/// skill §4.8 心得 9 测试宿主生产路径重写要点）：真实 DI（双扩展 ConfigureServices + FreeSql 基础设施 +
/// <c>IEntityDAC&lt;T&gt;</c> Singleton）→ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> →
/// 门面经 <c>user.Use&lt;接口&gt;()</c> AOP 路径解析（设 CurrentAopUser → GetRequiredService →
/// AddConstructibleService 守卫工厂 → ActivatorUtilities 直建）；DataService 经基类
/// <c>User.Use&lt;XxxDataService&gt;()</c> NoAop 直建（IEntityDAC 从 DI 解析，零手动注册——删除旧
/// <c>AddTestConstructibleDataService</c> 兜底工厂）。</para>
/// <para>⚠️ 边界保留组（对齐主包宿主，本批不改）：<see cref="InboxNotifier"/> 仍构造注入 <see cref="IDomainUser"/>
/// （多实现集合 TryAddEnumerable——普通 DI 解析 ctor(IDomainUser) 时 IDomainUser 永不注册 → 生产解析失败，
/// 框架缺口候选 T3，见批次 3 整改日志）；测试宿主为其注册可配置 <see cref="StubDomainUser"/>
/// （IDomainUser 单例——门面经守卫工厂走真实 <see cref="DomainUser{TUserInfo}"/>，桩仅供未整改的
/// InboxNotifier 经 DI 解析；其 <c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价实现）。</para>
/// </summary>
internal sealed class SignalRTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly DomainUser<TestUserInfo> _user;

    /// <summary>根 ServiceProvider（供直解非门面服务——SignalRNotifier/定义管理器等接线型）。</summary>
    public ServiceProvider Provider => _serviceProvider;

    /// <summary>真实绑定用户（生产 AOP 路径 CurrentAopUser 来源——门面解析入口）。</summary>
    public DomainUser<TestUserInfo> User => _user;

    /// <summary>发布器门面（生产 AOP 路径：User.Use&lt;INotificationPublisher&gt;() 经守卫工厂创建实例）。</summary>
    public INotificationPublisher Publisher => _user.Use<INotificationPublisher>();

    /// <summary>收件箱门面（生产 AOP 路径）。</summary>
    public INotificationStore Store => _user.Use<INotificationStore>();

    /// <summary>偏好管理门面（生产 AOP 路径）。</summary>
    public INotificationPreferenceManager PreferenceManager => _user.Use<INotificationPreferenceManager>();

    private SignalRTestHost(ServiceProvider serviceProvider, DomainUser<TestUserInfo> user)
    {
        _serviceProvider = serviceProvider;
        _user = user;
    }

    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（每次调用新连接 = 独立内存库）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>同步四张通知表结构（Notification / UserNotification / NotificationSubscription / NotificationPreference）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<NotificationEntity>();
        fsql.CodeFirst.SyncStructure<UserNotificationEntity>();
        fsql.CodeFirst.SyncStructure<NotificationSubscriptionEntity>();
        fsql.CodeFirst.SyncStructure<NotificationPreferenceEntity>();
    }

    /// <summary>
    /// 构建测试 DI 宿主（生产路径，V4.10.53 领域自治根治，对齐主包 <c>NotificationTestHost</c> 形态）：
    /// 注册日志 + 空配置桩 + 内存 fsql + IEntityDAC 基础设施（Singleton + 共享 UnitOfWorkManager——
    /// 消费方 DomainHost 等价注册）+ 定义 Provider + Fake IHubContext + 主包/SignalR 双 Initializer
    /// （<paramref name="configure"/> 在初始化器之前追加注册），然后 <c>DomainUser&lt;TestUserInfo&gt;.BindScope</c>
    /// 绑定解析作用域 + 创建真实 DomainUser。
    /// </summary>
    public static SignalRTestHost Build(IFreeSql fsql, FakeHubContext hub, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // BindConfiguration 惰性读取 IConfiguration（OptionsBuilder.Configure）——注册空配置桩（对齐 Tagging 测试先例）
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddSingleton(fsql);
        // IEntityDAC<T> 基础设施注册（消费方 DomainHost 等价注册——Singleton + 共享 UnitOfWorkManager，
        // 对齐 Settings/Account/MFA/主包 Notifications 生产路径宿主）
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<NotificationEntity>, FreeSqlEntityDAC<NotificationEntity>>();
        services.AddSingleton<IEntityDAC<UserNotificationEntity>, FreeSqlEntityDAC<UserNotificationEntity>>();
        services.AddSingleton<IEntityDAC<NotificationSubscriptionEntity>, FreeSqlEntityDAC<NotificationSubscriptionEntity>>();
        services.AddSingleton<IEntityDAC<NotificationPreferenceEntity>, FreeSqlEntityDAC<NotificationPreferenceEntity>>();
        // V0.2.0 VEntity：IEntityReadOnlyDAC 只读契约（手写只读 DataService 经基类 User.Use 直建时从 DI 解析）
        services.AddSingleton<IEntityReadOnlyDAC<UserNotificationView>, FreeSqlEntityDAC<UserNotificationView>>();
        services.AddSingleton<ITransactionManager>(new NoopTransactionManager());
        services.AddSingleton<INotificationDefinitionProvider, SignalRTestDefinitions>();
        services.AddSingleton<IHubContext<NotificationsHub>>(hub);

        // ⚠️ 边界保留组（对齐主包宿主，本批不改）：InboxNotifier 仍 ctor 注入 IDomainUser——多实现集合普通 DI
        // 解析需此桩（门面经守卫工厂走真实 DomainUser，此桩仅供未整改的 InboxNotifier 经 DI 解析——T3 候选记录）
        var stubUser = new StubDomainUser();
        services.AddSingleton<IDomainUser>(sp => { stubUser.ServiceProvider = sp; return stubUser; });

        configure?.Invoke(services);
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        new NotificationsSignalRExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var provider = services.BuildServiceProvider();

        // 生产路径：绑定解析作用域 + 真实 DomainUser（AOP 路径 CurrentAopUser 来源）
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("signalr-test-user", "SignalR 测试用户") };

        return new SignalRTestHost(provider, user);
    }

    /// <summary>解析服务（接线型服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    /// <summary>释放宿主。</summary>
    public void Dispose() => _serviceProvider.Dispose();
}

/// <summary>空事务管理器桩——SQLite 内存库单条写入自带原子性，测试无需物理事务。</summary>
internal sealed class NoopTransactionManager : ITransactionManager
{
    public ITransactionScope Begin(IsolationLevel isolationLevel = IsolationLevel.Serializable) => new NoopScope();
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

/// <summary>集成测试通知定义——SignalR 双通道用例。</summary>
internal sealed class SignalRTestDefinitions : INotificationDefinitionProvider
{
    public const string DualChannelNotice = "DualChannelNotice";   // Inbox + SignalR
    public const string SignalROnlyNotice = "SignalROnlyNotice";   // SignalR 单通道（基线）

    public void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(DualChannelNotice, "双通道公告")
            .UseChannels("Inbox", "SignalR"));
        context.Add(new NotificationDefinition(SignalROnlyNotice, "SignalR 单通道")
            .UseChannels("SignalR"));
    }
}

/// <summary>
/// 测试用户桩——实现 IDomainUser 最小契约（默认匿名，无租户）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价实现——具体类经
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c> 直建（IDomainUser 参数显式传 this，
/// IEntityDAC 等其余从 DI 解析——对齐主包 Notifications/Settings/Tagging 测试桩）。</para>
/// <para>⚠️ 本桩供未整改的 <see cref="InboxNotifier"/>（多实现集合成员仍 ctor 注入 IDomainUser——T3 候选）
/// 经 DI 解析；3 标准门面经真实 <see cref="DomainUser{TUserInfo}"/> + 守卫工厂 AOP 路径解析（不触此桩）。</para>
/// </summary>
internal sealed class StubDomainUser : IDomainUser
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
    public string? UserId => null;
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
    public System.Collections.Generic.IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}

/// <summary>测试最小用户类型——模拟消费方自定义 UserInfo。</summary>
public class TestUserInfo : SimpleUserInfo
{
    public TestUserInfo() : base() { }

    public TestUserInfo(string userIdString, string userName, params string[] roles)
        : base(userIdString, userName)
    {
        Roles = roles.ToList();
    }
}
