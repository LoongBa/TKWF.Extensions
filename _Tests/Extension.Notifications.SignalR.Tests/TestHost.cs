using System.Data;
using System.Threading;
using FreeSql;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// </summary>
internal static class SignalRTestHost
{
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
    /// 构建测试 DI 宿主：注册日志 + 内存 fsql + 定义 Provider + 主包/SignalR 双 Initializer
    /// + Fake IHubContext + DataService 兜底工厂（镜像生产可构造工厂——测试用户源 = DI IDomainUser）。
    /// </summary>
    public static ServiceProvider Build(IFreeSql fsql, FakeHubContext hub, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // BindConfiguration 惰性读取 IConfiguration（OptionsBuilder.Configure）——注册空配置桩（对齐 Tagging 测试先例）
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddSingleton(fsql);
        services.AddSingleton<ITransactionManager>(new NoopTransactionManager());
        services.AddSingleton<INotificationDefinitionProvider, SignalRTestDefinitions>();
        services.AddSingleton<IHubContext<NotificationsHub>>(hub);
        services.AddScoped<UnitOfWorkManager>();
        services.AddScoped<IEntityDAC<NotificationEntity>>(sp => new FreeSqlEntityDAC<NotificationEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<UserNotificationEntity>>(sp => new FreeSqlEntityDAC<UserNotificationEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<NotificationSubscriptionEntity>>(sp => new FreeSqlEntityDAC<NotificationSubscriptionEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<NotificationPreferenceEntity>>(sp => new FreeSqlEntityDAC<NotificationPreferenceEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IDomainUser>(_ => new StubDomainUser());
        AddTestConstructibleDataService<NotificationEntityDataService>(services);
        AddTestConstructibleDataService<UserNotificationEntityDataService>(services);
        AddTestConstructibleDataService<NotificationSubscriptionEntityDataService>(services);
        AddTestConstructibleDataService<NotificationPreferenceEntityDataService>(services);
        // V0.2.0 VEntity：IEntityReadOnlyDAC 只读契约 + 手写只读 DataService（NotificationStore 依赖，同路径 DI 兜底工厂）
        services.AddScoped<IEntityReadOnlyDAC<UserNotificationView>>(sp => new FreeSqlEntityDAC<UserNotificationView>(sp.GetRequiredService<UnitOfWorkManager>()));
        AddTestConstructibleDataService<UserNotificationViewDataService>(services);
        configure?.Invoke(services);
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        new NotificationsSignalRExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>测试版可构造 DataService 工厂——镜像生产 AddConstructibleDataService（ActivatorUtilities + 域用户）。</summary>
    private static void AddTestConstructibleDataService<T>(IServiceCollection services)
        where T : class
    {
        services.AddScoped<T>(sp =>
        {
            var user = sp.GetRequiredService<IDomainUser>();
            return (T)ActivatorUtilities.CreateInstance(sp, typeof(T), user);
        });
    }
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

/// <summary>测试最小用户类型——模拟消费方自定义 UserInfo。</summary>
public class TestUserInfo : SimpleUserInfo
{
    public TestUserInfo() : base() { }
}