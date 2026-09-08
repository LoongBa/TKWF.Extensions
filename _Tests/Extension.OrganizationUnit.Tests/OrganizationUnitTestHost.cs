using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.OrganizationUnit;

namespace TKWF.Ext.OrganizationUnit.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库创建 + 表结构同步。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与 Approval/DataDictionary 测试同模式）。</para>
/// </summary>
internal static class OrganizationUnitTestSupport
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（自动同步表结构）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
        => new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    /// <summary>同步两张表结构（OrganizationUnit + OrganizationUnitUser）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<OrganizationUnitEntity>();
        fsql.CodeFirst.SyncStructure<OrganizationUnitUserEntity>();
    }
}

/// <summary>
/// 完整测试宿主——构建 DI 容器：真实 DataService 链（FreeSqlEntityDAC + UnitOfWorkManager 驱动）
/// + ITransactionManager（Noop，对齐 Approval 测试宿主）+ 扩展初始化器注册 Store/Manager（TryAddScoped）。
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库实例（用例隔离）。</para>
/// </summary>
internal sealed class OrganizationUnitTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public IFreeSql Fsql { get; }

    public IOrganizationUnitManager Manager => _serviceProvider.GetRequiredService<IOrganizationUnitManager>();

    public IOrganizationUnitStore Store => _serviceProvider.GetRequiredService<IOrganizationUnitStore>();

    public OrganizationUnitEntityDataService OuDataService => _serviceProvider.GetRequiredService<OrganizationUnitEntityDataService>();

    public OrganizationUnitUserEntityDataService UserDataService => _serviceProvider.GetRequiredService<OrganizationUnitUserEntityDataService>();

    private OrganizationUnitTestHost(ServiceProvider serviceProvider, IFreeSql fsql)
    {
        _serviceProvider = serviceProvider;
        Fsql = fsql;
    }

    /// <summary>全新宿主（每次调用独立 SQLite 内存库）。</summary>
    public static OrganizationUnitTestHost Create(Action<IServiceCollection>? configure = null)
    {
        var fsql = OrganizationUnitTestSupport.CreateInMemoryFreeSql();
        OrganizationUnitTestSupport.SyncStructure(fsql);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);

        var stubUser = new StubDomainUser();
        services.AddSingleton<IDomainUser>(stubUser);

        // DataService 链（真实 FreeSql DAC——红线合规委托路径）
        services.AddSingleton(new OrganizationUnitEntityDataService(
            stubUser, new FreeSqlEntityDAC<OrganizationUnitEntity>(new UnitOfWorkManager(fsql))));
        services.AddSingleton(new OrganizationUnitUserEntityDataService(
            stubUser, new FreeSqlEntityDAC<OrganizationUnitUserEntity>(new UnitOfWorkManager(fsql))));

        // ITransactionManager（Move/Delete/Create 写路径事务包裹依赖——Noop Begin/Commit 空操作，
        // DataService 逐操作经 UnitOfWorkManager 持久化；对齐 Approval 测试宿主共识）
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // 扩展初始化器注册 IOrganizationUnitStore / IOrganizationUnitManager（TryAddScoped 不覆盖已注册 DataService）
        new OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>().ConfigureServices(services);

        configure?.Invoke(services);

        return new OrganizationUnitTestHost(services.BuildServiceProvider(), fsql);
    }

    /// <summary>解析服务（Scoped 服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    public void Dispose() => _serviceProvider.Dispose();
}

/// <summary>Noop 事务管理器——BeginAsync/CommitAsync 空操作（对齐 Approval 测试宿主）。</summary>
internal sealed class NoopTransactionManager : ITransactionManager
{
    public bool IsActive => false;

    public ITransactionScope Begin(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Serializable)
        => new NoopTransactionScope();

    public Task<ITransactionScope> BeginAsync(
        System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Serializable,
        CancellationToken ct = default)
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
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}