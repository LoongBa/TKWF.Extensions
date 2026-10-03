using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// 测试公共设施——StubDomainUser + 基于 FreeSqlEntityDAC 的 Store/QueryService 工厂。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与 Settings/BlobStoring 测试同模式）。</para>
/// <para>ADR88/DI004（A 批整改）：Store/QueryService/AnalyticsService 构造不再注入 DataService——经
/// <c>User.Use&lt;T&gt;()</c> 懒加载解析。测试工厂构建 StubDomainUser + 能力容器（注册真实 DAC 驱动
/// 的 DataService 实例），注入 ServiceProvider。</para>
/// </summary>
internal static class AuditLoggingTestHost
{
    /// <summary>创建基于 SQLite 内存库的 AuditLogEntityDataService（真实 FreeSql DAC 驱动）。</summary>
    public static AuditLogEntityDataService CreateDataService(IFreeSql fsql, IDomainUser? user = null)
    {
        var dac = new FreeSqlEntityDAC<AuditLogEntity>(new UnitOfWorkManager(fsql));
        return new AuditLogEntityDataService(user ?? new StubDomainUser(), dac);
    }

    /// <summary>构建 StubDomainUser + 能力容器（direct-new 工厂模式）：注册真实 DAC 驱动的 DataService 实例。</summary>
    private static StubDomainUser CreateStub(IFreeSql fsql, Action<IServiceCollection, StubDomainUser> register)
    {
        var stub = new StubDomainUser();
        var services = new ServiceCollection();
        register(services, stub);
        stub.ServiceProvider = services.BuildServiceProvider();
        return stub;
    }

    /// <summary>创建基于 SQLite 内存库的 AuditLogStore（DataService 委托）。</summary>
    public static AuditLogStore CreateStore(IFreeSql fsql)
    {
        var stub = CreateStub(fsql, (s, user) =>
            s.AddSingleton(CreateDataService(fsql, user)));
        return new AuditLogStore(stub, NullLogger<AuditLogStore>.Instance);
    }

    /// <summary>创建基于 SQLite 内存库的 AuditLogQueryService（DataService 委托）。</summary>
    public static AuditLogQueryService CreateQueryService(IFreeSql fsql)
    {
        var stub = CreateStub(fsql, (s, user) =>
            s.AddSingleton(CreateDataService(fsql, user)));
        return new AuditLogQueryService(stub, NullLogger<AuditLogQueryService>.Instance);
    }

    /// <summary>创建基于 SQLite 内存库的 AuditLogAnalyticsService（DataService 委托 + Options + 空日志）。</summary>
    public static AuditLogAnalyticsService CreateAnalyticsService(
        IFreeSql fsql, AuditLoggingOptions? options = null)
    {
        var stub = CreateStub(fsql, (s, user) =>
            s.AddSingleton(CreateDataService(fsql, user)));
        return new AuditLogAnalyticsService(stub,
            new OptionsWrapper<AuditLoggingOptions>(options ?? new AuditLoggingOptions()),
            NullLogger<AuditLogAnalyticsService>.Instance);
    }
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。
/// <para>ADR88/DI004（A 批整改）：Use&lt;T&gt;()/GetService&lt;T&gt;() 从注入的 ServiceProvider 容器解析
/// （测试工厂经 <see cref="ServiceProvider"/> 注入；未注入时保留原抛 NotSupportedException 语义）。</para></summary>
internal sealed class StubDomainUser : IDomainUser
{
    private IServiceProvider? _provider;
    private readonly object _gate = new();
    private readonly Dictionary<Type, object?> _cache = new();

    /// <summary>ServiceProvider（测试工厂注册时注入——懒加载 Use&lt;T&gt; 解析源，线程安全）。</summary>
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
        lock (_gate) provider = _provider ?? throw new NotSupportedException("Stub: Use<T> not supported in unit tests");
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
        lock (_gate) provider = _provider ?? throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");
        return provider.GetRequiredService<TService>();
    }

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}