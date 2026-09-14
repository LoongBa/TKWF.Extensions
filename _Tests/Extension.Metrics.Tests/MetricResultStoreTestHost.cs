using System;
using FreeSql;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// v0.2.0 持久化契约测试公共设施——SQLite 内存库 + 真实 <see cref="FreeSqlEntityDAC{TEntity}"/> 驱动
/// <see cref="TestMetricResultEntityDataService"/>（红线合规测试模式，对齐既有扩展测试先例：
/// AuditLogging <c>AuditLoggingTestHost</c>——真实 DAC 而非内存桩）。
/// </summary>
internal static class MetricResultStoreTestHost
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql + 同步 TestMetricResult 表结构（每次调用新连接 = 独立内存库）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<TestMetricResultEntity>();
        return fsql;
    }

    /// <summary>创建基于 SQLite 内存库的消费方 Store（DataService 委托路径，红线合规）。</summary>
    public static TestMetricResultStore CreateStore(IFreeSql fsql)
        => new(CreateDataService(fsql));

    /// <summary>创建基于 SQLite 内存库的 DataService（真实 FreeSql DAC 驱动）。</summary>
    public static TestMetricResultEntityDataService CreateDataService(IFreeSql fsql)
    {
        var dac = new FreeSqlEntityDAC<TestMetricResultEntity>(new UnitOfWorkManager(fsql));
        return new TestMetricResultEntityDataService(new StubDomainUser(), dac);
    }
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户；对齐既有扩展测试 StubDomainUser）。</summary>
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