using System;
using System.Collections.Generic;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// v0.2.0 持久化契约测试公共设施——SQLite 内存库 + 真实 <see cref="FreeSqlEntityDAC{TEntity}"/> 驱动
/// <see cref="TestMetricResultEntityDataService"/>（红线合规测试模式，对齐既有扩展测试先例：
/// AuditLogging <c>AuditLoggingTestHost</c>——真实 DAC 而非内存桩）。
/// <para>ADR88/DI004：Store 构造不再注入 DataService——经 User.Use&lt;T&gt;() 懒加载（扩展侧测试桩整改）。</para>
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

    /// <summary>创建基于 SQLite 内存库的消费方 Store（DataService 懒加载经 User.Use 解析，红线合规）。</summary>
    public static TestMetricResultStore CreateStore(IFreeSql fsql)
    {
        var stub = new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton(new TestMetricResultEntityDataService(stub, new FreeSqlEntityDAC<TestMetricResultEntity>(new UnitOfWorkManager(fsql))));
        stub.ServiceProvider = services.BuildServiceProvider();
        return new TestMetricResultStore(stub);
    }

    /// <summary>创建基于 SQLite 内存库的 DataService（真实 FreeSql DAC 驱动）。</summary>
    public static TestMetricResultEntityDataService CreateDataService(IFreeSql fsql)
    {
        var dac = new FreeSqlEntityDAC<TestMetricResultEntity>(new UnitOfWorkManager(fsql));
        return new TestMetricResultEntityDataService(new StubDomainUser(), dac);
    }
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户；对齐既有扩展测试 StubDomainUser）。
/// <para>ADR88/DI004：Use&lt;T&gt;()/GetService&lt;T&gt;() 从注入的 ServiceProvider 容器解析
/// （测试工厂经 <see cref="ServiceProvider"/> 注入）。</para></summary>
internal sealed class StubDomainUser : IDomainUser
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object?> _cache = new();
    private IServiceProvider? _provider;

    /// <summary>ServiceProvider（测试工厂注入时设置——懒加载 Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider ServiceProvider
    {
        set { lock (_gate) _provider = value; }
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
        IServiceProvider provider;
        lock (_gate) provider = _provider ?? throw new NotSupportedException("Stub: Provider 未注入");
        if (_cache.TryGetValue(typeof(TDomainService), out var cached) && cached is TDomainService svc)
            return svc;
        lock (_gate)
        {
            if (_cache.TryGetValue(typeof(TDomainService), out cached) && cached is TDomainService svc2)
                return svc2;
            var resolved = provider.GetRequiredService<TDomainService>();
            _cache[typeof(TDomainService)] = resolved;
            return resolved;
        }
    }

    public TService GetService<TService>() where TService : notnull
    {
        IServiceProvider provider;
        lock (_gate) provider = _provider ?? throw new NotSupportedException("Stub: Provider 未注入");
        return provider.GetRequiredService<TService>();
    }
    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}