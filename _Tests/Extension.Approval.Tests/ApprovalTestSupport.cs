using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain.Events;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Approval;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// 测试共享支撑——SQLite 内存库 + DataService 构造 + StubDomainUser + 完整 ApprovalTestHost。
/// <para>ApprovalManager 经三个 DataService（FreeSqlEntityDAC + UnitOfWorkManager 驱动）委托持久化。
/// NoopTransactionManager 用于事件派发路径测试（CommitAsync 空操作）。
/// </para>
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

    /// <summary>同步三张表结构（Flow + Instance + Task）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<ApprovalFlowEntity>();
        fsql.CodeFirst.SyncStructure<ApprovalInstanceEntity>();
        fsql.CodeFirst.SyncStructure<ApprovalTaskEntity>();
    }

    /// <summary>构造 ApprovalTestHost（完整 DI 容器 + 真实 DataService + NoopTransactionManager + 事件收集）。</summary>
    public static ApprovalTestHost Build(IFreeSql fsql, Action<IServiceCollection>? configure = null)
        => new(fsql, configure);
}

/// <summary>
/// 完整测试宿主——构建 DI 容器，注册 DataService 链 + ApprovalManager + NoopTransactionManager + EventCollector。
/// </summary>
internal sealed class ApprovalTestHost : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IFreeSql _fsql;

    public IApprovalService ApprovalService => _serviceProvider.GetRequiredService<IApprovalService>();
    public IApprovalQueryService QueryService => _serviceProvider.GetRequiredService<IApprovalQueryService>();
    public IApprovalAssigneeResolver Resolver => _serviceProvider.GetRequiredService<IApprovalAssigneeResolver>();
    public ApprovalFlowEntityDataService FlowDataService => _serviceProvider.GetRequiredService<ApprovalFlowEntityDataService>();
    public ApprovalInstanceEntityDataService InstanceDataService => _serviceProvider.GetRequiredService<ApprovalInstanceEntityDataService>();
    public ApprovalTaskEntityDataService TaskDataService => _serviceProvider.GetRequiredService<ApprovalTaskEntityDataService>();
    public EventCollector Events => _serviceProvider.GetRequiredService<EventCollector>();

    public ApprovalTestHost(IFreeSql fsql, Action<IServiceCollection>? configure = null)
    {
        _fsql = fsql;
        var services = new ServiceCollection();

        // 日志
        services.AddLogging();

        // FreeSql 单例
        services.AddSingleton(fsql);

        // 事件收集器（替代真实 ILocalEventBus——收集已派发事件供断言）
        services.AddSingleton<EventCollector>();
        services.AddSingleton<ILocalEventBus>(sp => sp.GetRequiredService<EventCollector>());

        // NoopTransactionManager（CommitAsync 空操作——不回滚即可）
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // DataService 链（真实 FreeSql DAC）
        var stubUser = new StubDomainUser();
        services.AddSingleton<IDomainUser>(stubUser);
        services.AddSingleton(sp =>
            new ApprovalFlowEntityDataService(
                stubUser, new FreeSqlEntityDAC<ApprovalFlowEntity>(new UnitOfWorkManager(fsql))));
        services.AddSingleton(sp =>
            new ApprovalInstanceEntityDataService(
                stubUser, new FreeSqlEntityDAC<ApprovalInstanceEntity>(new UnitOfWorkManager(fsql))));
        services.AddSingleton(sp =>
            new ApprovalTaskEntityDataService(
                stubUser, new FreeSqlEntityDAC<ApprovalTaskEntity>(new UnitOfWorkManager(fsql))));

        // 默认审批人解析器
        services.TryAddScoped<IApprovalAssigneeResolver, DefaultApprovalAssigneeResolver>();

        // ApprovalManager + ApprovalQueryService
        services.TryAddScoped<ApprovalManager>();
        services.TryAddScoped<IApprovalService>(sp => sp.GetRequiredService<ApprovalManager>());
        services.TryAddScoped<ApprovalQueryService>();
        services.TryAddScoped<IApprovalQueryService>(sp => sp.GetRequiredService<ApprovalQueryService>());

        configure?.Invoke(services);

        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>解析 Scoped 服务。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    public void Dispose() => (_serviceProvider as IDisposable)?.Dispose();
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

/// <summary>最小 IDomainUser 桩。</summary>
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
        => throw new NotSupportedException("Stub: Use<T> not supported");
    public TService GetService<TService>() where TService : notnull
        => throw new NotSupportedException("Stub: GetService<T> not supported");
    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
