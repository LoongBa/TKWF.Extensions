using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Events;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Approval;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// 测试共享支撑——SQLite 内存库 + 完整 ApprovalTestHost（V0.3.0 走生产路径）。
/// <para>V0.3.0（V4.10.53 ADR90 领域自治根治，正确路线）：测试宿主弃 StubDomainUser + 手写 DataService 单例注册
/// （旧形态掩盖 IDomainUser 永不注册 DI 的生产失败）——真实 DI（Initializer <c>ConfigureServices</c> + FreeSql
/// SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> +
/// <c>User.Use&lt;接口&gt;()</c> AOP 路径解析（设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂）；
/// DataService 经基类 <c>User.Use&lt;具体类&gt;()</c> NoAop 路径直建（ActivatorUtilities，IEntityDAC 从 DI 解析）。</para>
/// </summary>
internal static class ApprovalTestSupport
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>同步五张表结构 + VEntity 真实视图（Flow + Instance + Task + Append + CC + vw_ApprovalTaskView）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<ApprovalFlowEntity>();
        fsql.CodeFirst.SyncStructure<ApprovalInstanceEntity>();
        fsql.CodeFirst.SyncStructure<ApprovalTaskEntity>();
        fsql.CodeFirst.SyncStructure<ApprovalAppendEntity>();
        fsql.CodeFirst.SyncStructure<ApprovalCCEntity>();
        // V0.3.0 VEntity：建真实视图（SQLite 方言，来自 ApprovalTaskView.ViewSqlSQLite）——不跑宿主 SyncViewsAsync。
        // 注意：Approval 全部 DateTime 列（非 DateTimeOffset），SQLite 直接引用真实列，无需 NULL 占位
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""TKWFV_ApprovalTaskView"" AS
SELECT t.""Id"", t.""InstanceId"", t.""StepIndex"", t.""StepName"", t.""ApproverType"", t.""ApproverValue"",
       t.""ApproverUserId"", t.""Status"", t.""ApprovedAt"", t.""ApprovedBy"", t.""Comment"", t.""TransferredTo"",
       t.""CreateTime"" AS ""TaskCreateTime"", i.""BusinessType"", i.""BusinessId"", i.""Status"" AS ""InstanceStatus"",
       i.""IsActive"", i.""CurrentStepIndex"", i.""Submitter"", i.""FlowCode"", i.""CreateTime"" AS ""InstanceCreateTime""
FROM ""TKWF_ApprovalTask"" t
INNER JOIN ""TKWF_ApprovalInstance"" i ON t.""InstanceId"" = i.""Id""");
    }

    /// <summary>构造 ApprovalTestHost（V0.3.0 生产路径——真实 DI + BindScope + User.Use 解析）。</summary>
    public static ApprovalTestHost Build(IFreeSql fsql, Action<IServiceCollection>? configure = null)
        => new(fsql, configure);
}

/// <summary>
/// 完整测试宿主（V0.3.0 生产路径）——真实 DI 容器（扩展 ConfigureServices + FreeSql 基础设施 +
/// NoopTransactionManager + EventCollector）+ <see cref="DomainUser{TUserInfo}"/> 域作用域绑定。
/// </summary>
internal sealed class ApprovalTestHost : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DomainUser<TestUserInfo> _user;

    // 门面/Resolver/DataService 全部经 User.Use<T>() 解析（生产路径）：
    // 接口 → AOP 路径（设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂 / TryAddEnumerable 集合）；
    // 具体类 DataService → NoAop 路径（ActivatorUtilities 直建，IEntityDAC 从 DI 解析）。
    public IApprovalService ApprovalService => _user.Use<IApprovalService>();
    public IApprovalQueryService QueryService => _user.Use<IApprovalQueryService>();
    public IApprovalAssigneeResolver Resolver => _user.Use<IApprovalAssigneeResolver>();
    public IApprovalTimeoutService TimeoutService => _user.Use<IApprovalTimeoutService>();
    public ApprovalFlowEntityDataService FlowDataService => _user.Use<ApprovalFlowEntityDataService>();
    public ApprovalInstanceEntityDataService InstanceDataService => _user.Use<ApprovalInstanceEntityDataService>();
    public ApprovalTaskEntityDataService TaskDataService => _user.Use<ApprovalTaskEntityDataService>();
    public ApprovalAppendEntityDataService AppendDataService => _user.Use<ApprovalAppendEntityDataService>();
    public ApprovalCCEntityDataService CcDataService => _user.Use<ApprovalCCEntityDataService>();
    public ApprovalTaskViewDataService TaskViewDataService => _user.Use<ApprovalTaskViewDataService>();
    public EventCollector Events => _serviceProvider.GetRequiredService<EventCollector>();

    public ApprovalTestHost(IFreeSql fsql, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        // 1. 扩展装配——生产形态（消费方白名单启用后三钩子执行 ConfigureServices）
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 2. 日志
        services.AddLogging();

        // 3. FreeSql 基础设施（消费方 DomainHost 等价注册）
        services.AddSingleton(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<ApprovalFlowEntity>, FreeSqlEntityDAC<ApprovalFlowEntity>>();
        services.AddSingleton<IEntityDAC<ApprovalInstanceEntity>, FreeSqlEntityDAC<ApprovalInstanceEntity>>();
        services.AddSingleton<IEntityDAC<ApprovalTaskEntity>, FreeSqlEntityDAC<ApprovalTaskEntity>>();
        services.AddSingleton<IEntityDAC<ApprovalAppendEntity>, FreeSqlEntityDAC<ApprovalAppendEntity>>();
        services.AddSingleton<IEntityDAC<ApprovalCCEntity>, FreeSqlEntityDAC<ApprovalCCEntity>>();
        // V0.3.0 VEntity：只读 DAC（DI 严格按请求类型匹配，IEntityDAC 子接口不自动匹配父接口——显式注册）
        services.AddSingleton<IEntityReadOnlyDAC<ApprovalTaskView>, FreeSqlEntityDAC<ApprovalTaskView>>();

        // 4. 事件收集器（替代真实 ILocalEventBus——收集已派发事件供断言）+ Noop 事务管理器（CommitAsync 空操作）
        services.AddSingleton<EventCollector>();
        services.AddSingleton<ILocalEventBus>(sp => sp.GetRequiredService<EventCollector>());
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        configure?.Invoke(services);

        _serviceProvider = services.BuildServiceProvider();
        // 5. 解析作用域绑定（生产经 DomainHost.NewDomainContext 绑定 AsyncLocal——测试等价 BindScope）
        DomainUser<TestUserInfo>.BindScope(_serviceProvider);
        _user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("test-user", "测试用户") };
    }

    /// <summary>解析 Scoped 服务（基础设施/事件收集器——域服务一律经 User.Use&lt;T&gt;()）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        DomainUser<TestUserInfo>.UnBindScope();
        (_serviceProvider as IDisposable)?.Dispose();
    }
}

/// <summary>
/// 事件收集器——替代真实 ILocalEventBus，收集已派发的事件供测试断言。
/// </summary>
internal sealed class EventCollector : ILocalEventBus
{
    public List<object> PublishedEvents { get; } = [];

    public Task PublishAsync<TEvent>(TEvent eventData) where TEvent : class
    {
        PublishedEvents.Add(eventData!);
        return Task.CompletedTask;
    }

    public Task PostAsync<TEvent>(TEvent eventData) where TEvent : class
    {
        PublishedEvents.Add(eventData!);
        return Task.CompletedTask;
    }

    public Task PublishAsync(Type eventType, object eventData)
    {
        PublishedEvents.Add(eventData);
        return Task.CompletedTask;
    }

    public IDisposable Subscribe<TEvent>(ILocalEventHandler<TEvent> handler) where TEvent : class
        => new NoopDisposable();
}

/// <summary>Noop 事务管理器——CommitAsync 空操作，BeginAsync 返回 NoopTransactionScope。</summary>
internal sealed class NoopTransactionManager : ITransactionManager
{
    public bool IsActive => false;
    public ITransactionScope Begin(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Serializable)
        => new NoopTransactionScope();
    public Task<ITransactionScope> BeginAsync(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Serializable, CancellationToken ct = default)
        => Task.FromResult<ITransactionScope>(new NoopTransactionScope());
}

/// <summary>Noop 事务作用域——CommitAsync/RollbackAsync 空操作。</summary>
internal sealed class NoopTransactionScope : ITransactionScope
{
    public bool IsActive => true;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Commit() { }
    public void Rollback() { }
}

/// <summary>Noop IDisposable。</summary>
internal sealed class NoopDisposable : IDisposable { public void Dispose() { } }
