using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Calendar;

namespace TKWF.Ext.Calendar.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库创建 + 表结构同步。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;</c> 驱动（与 OrganizationUnit/Approval/DataDictionary 测试同模式）；
/// v4.10.8 (ADR61) 起 DataService 经测试版可构造工厂（DI 兜底，镜像生产 AddConstructibleDataService）注册。</para>
/// </summary>
internal static class CalendarTestSupport
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（自动同步表结构）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
        => new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    /// <summary>同步两张表结构（Calendar + CalendarEvent）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<CalendarEntity>();
        fsql.CodeFirst.SyncStructure<CalendarEventEntity>();
    }
}

/// <summary>
/// 完整测试宿主——构建 DI 容器：DataService（v4.10.8 ADR61 DI 兜底工厂注册：
/// ActivatorUtilities.CreateInstance + DI IDomainUser）+ ITransactionManager（Noop，对齐 OrganizationUnit 测试宿主）
/// + 扩展初始化器注册 Store/Manager（TryAddScoped）。
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库实例（用例隔离）。
/// <paramref name="configure"/> 回调在初始化器之后执行——事务记录型测试可覆盖 ITransactionManager
/// （RemoveAll + AddSingleton Recording，对齐 OrganizationUnitTransactionTests 模式）。</para>
/// </summary>
internal sealed class CalendarTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public IFreeSql Fsql { get; }

    public ICalendarManager Manager => _serviceProvider.GetRequiredService<ICalendarManager>();

    public ICalendarStore Store => _serviceProvider.GetRequiredService<ICalendarStore>();

    public CalendarEntityDataService CalendarDataService => _serviceProvider.GetRequiredService<CalendarEntityDataService>();

    public CalendarEventEntityDataService EventDataService => _serviceProvider.GetRequiredService<CalendarEventEntityDataService>();

    private CalendarTestHost(ServiceProvider serviceProvider, IFreeSql fsql)
    {
        _serviceProvider = serviceProvider;
        Fsql = fsql;
    }

    /// <summary>全新宿主（每次调用独立 SQLite 内存库）。</summary>
    public static CalendarTestHost Create(Action<IServiceCollection>? configure = null)
    {
        var fsql = CalendarTestSupport.CreateInMemoryFreeSql();
        CalendarTestSupport.SyncStructure(fsql);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);

        var stubUser = new StubDomainUser();
        services.AddSingleton<IDomainUser>(stubUser);

        // v4.10.8 (ADR61) 迁移：模拟生产 DataService 自动注册——测试容器不走消费方 SG 聚合，
        // 用与生产同构的 DI 兜底工厂（镜像 AddConstructibleDataService：ActivatorUtilities.CreateInstance
        // + 域用户；测试用户源 = DI IDomainUser 而非 AsyncLocal CurrentAopUser——免域作用域，xUnit 并行安全）。
        services.AddScoped<UnitOfWorkManager>();
        services.AddScoped<IEntityDAC<CalendarEntity>>(sp => new FreeSqlEntityDAC<CalendarEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddScoped<IEntityDAC<CalendarEventEntity>>(sp => new FreeSqlEntityDAC<CalendarEventEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        AddTestConstructibleDataService<CalendarEntityDataService>(services);
        AddTestConstructibleDataService<CalendarEventEntityDataService>(services);

        // ITransactionManager（Create/Update/Delete 写路径事务包裹依赖——Noop Begin/Commit 空操作，
        // DataService 逐操作经 UnitOfWorkManager 持久化；对齐 OrganizationUnit 测试宿主共识）
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // 扩展初始化器注册 ICalendarStore / ICalendarManager / DataService（TryAddScoped 不覆盖已注册 DataService）
        new CalendarExtensionInitializer<CalendarUserInfo>().ConfigureServices(services);

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        // ADR88/DI004：StubDomainUser 注入能力容器——懒加载 Use<T>() 经 provider 解析。
        if (provider.GetService<IDomainUser>() is StubDomainUser stub) stub.ServiceProvider = provider;
        return new CalendarTestHost(provider, fsql);
    }

    /// <summary>v4.10.8 (ADR61) 迁移：测试版可构造 DataService 工厂——镜像生产
    /// <c>AddConstructibleDataService</c>（<c>ActivatorUtilities.CreateInstance</c> + 域用户），
    /// 用户源改为 DI <c>IDomainUser</c>（StubDomainUser）而非 AsyncLocal <c>CurrentAopUser</c>——
    /// 免域作用域、xUnit 并行隔离安全（不设 AsyncLocal）。</summary>
    private static void AddTestConstructibleDataService<T>(IServiceCollection services)
        where T : class
    {
        services.AddScoped<T>(sp =>
        {
            var user = sp.GetRequiredService<IDomainUser>();
            return (T)ActivatorUtilities.CreateInstance(sp, typeof(T), user);
        });
    }

    /// <summary>解析服务（Scoped 服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    public void Dispose() => _serviceProvider.Dispose();
}

/// <summary>Noop 事务管理器——BeginAsync/CommitAsync 空操作（对齐 OrganizationUnit 测试宿主）。</summary>
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

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。
/// <para>ADR88/DI004：Use&lt;T&gt;()/GetService&lt;T&gt;() 从注入的 ServiceProvider 容器解析
/// （测试宿主构建后经 <see cref="ServiceProvider"/> 注入；未注入时抛 NotSupportedException）。</para></summary>
internal sealed class StubDomainUser : IDomainUser
{
    private IServiceProvider? _provider;
    private readonly object _gate = new();
    private readonly Dictionary<Type, object?> _cache = new();

    /// <summary>ServiceProvider（宿主构建后注入——懒加载 Use&lt;T&gt; 解析源，线程安全）。</summary>
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

/// <summary>
/// DateTime Kind 容错断言（SQLite 陷阱）——FreeSql SQLite 把 UTC DateTime 存为本地墙钟（无 Kind 标识），
/// 读出为 Unspecified（UTC 09:00 → 本地墙钟 17:00）。还原真实 UTC：<c>Unspecified</c> 一律按"本地墙钟
/// 语义"转 UTC（对齐 Store.NormalizeUtc / Notifications AssertRecent 模式——SpecifyKind(Local).ToUniversalTime）；
/// 仅 <c>Utc</c> 直通。统一 <see cref="Equal"/> 比较期望 UTC 与读出值。
/// </summary>
internal static class UtcAssert
{
    /// <summary>归一化到 UTC：Unspecified 视为本地墙钟（SQLite 存储语义）转 UTC；Utc 直通。</summary>
    public static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
            _ => value.ToUniversalTime(),
        };

    /// <summary>断言实际读出时间与期望 UTC 相等（Kind 容错）。</summary>
    public static void Equal(DateTime expectedUtc, DateTime actual)
        => Assert.Equal(expectedUtc, NormalizeUtc(actual));
}
