using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.OrganizationUnit;

namespace TKWF.Ext.OrganizationUnit.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库创建 + 表结构同步。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;</c> 驱动（与 Approval/DataDictionary 测试同模式）。</para>
/// </summary>
internal static class OrganizationUnitTestSupport
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（自动同步表结构）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
        => new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    /// <summary>同步两张表结构 + VEntity 真实视图（OrganizationUnit + OrganizationUnitUser + vw_UserOrganizationUnitView）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<OrganizationUnitEntity>();
        fsql.CodeFirst.SyncStructure<OrganizationUnitUserEntity>();
        // V0.2.0 VEntity：建真实视图（SQLite 方言，同 UserOrganizationUnitView.ViewSqlSQLite）——不跑宿主 SyncViewsAsync。
        // 注：视图只投影 OU/OUUser 业务列（无 DateTimeOffset），SQLite 直接引用真实列，无需 NULL 占位
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""vw_UserOrganizationUnitView"" AS
SELECT ouu.""Id"", ouu.""OrganizationUnitId"", ouu.""UserId"", ou.""Path"" AS ""OUPath"", ou.""Code"" AS ""OUCode"",
       ou.""Level"" AS ""OULevel"", ou.""Name"" AS ""OUName""
FROM ""OrganizationUnitUser"" ouu
INNER JOIN ""OrganizationUnit"" ou ON ouu.""OrganizationUnitId"" = ou.""Id""");
    }
}

/// <summary>
/// 完整测试宿主——V4.10.53（领域自治根治，ADR90，正确路线）重写走<b>生产路径</b>：
/// <list type="bullet">
/// <item><b>真实 DI</b>（Initializer <c>ConfigureServices</c> + FreeSql 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c>；</item>
/// <item><b>门面经真实 <c>DomainUser&lt;TestUserInfo&gt;.Use&lt;IOrganizationUnitManager&gt;()</c> AOP 路径解析</b>
///     （框架 Use 内部设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂 →
///     ActivatorUtilities 直建实现）——<see cref="Manager"/>（匿名——IDomainUser 永不注册 DI 语义保持，
///     Manager 经守卫工厂由 CurrentAopUser 传入，非 DI 解析）；</item>
/// <item><b>内部接线型 Store 普通 DI 解析</b>（TryAddScoped ImplementationType——Manager 守卫工厂 ActivatorUtilities
///     ctor 注入解析；实现 ctor(<see cref="IServiceProvider"/>) + C1 延迟解析 DataService）——<see cref="Store"/>；</item>
/// <item><b>DataService 可构造注册</b>（接线型 Store 的 C1 延迟解析源——测试镜像生产 ADR61 可构造工厂，
///     非 throw-factory；ctor 需 IDomainUser + IEntityDAC 均从 DI 解析）；</item>
/// <item><b>IDomainUser 注册（测试桥接）</b>：DataService ctor 需 IDomainUser（测试注册 <see cref="TestDomainUser"/> 桩——
///     生产 DataService 经 Use&lt;具体类&gt;() NoAop 直建不经 DI；接线型 Store 的 GetRequiredService 需要）。</item>
/// </list>
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库实例（用例隔离）。
/// <c>configure</c> 回调在扩展初始化器之后执行——可覆盖 ITransactionManager（对齐 Calendar/FileManagement 宿主模式）。</para>
/// </summary>
internal sealed class OrganizationUnitTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    /// <summary>生产 AOP 路径用户（真实 DomainUser&lt;TestUserInfo&gt;，匿名——经框架 Use 设 CurrentAopUser）。</summary>
    private readonly DomainUser<TestUserInfo> _domainUser = new();

    public IFreeSql Fsql { get; }

    /// <summary>门面——生产 AOP 路径：真实 DomainUser&lt;TestUserInfo&gt;.Use&lt;IOrganizationUnitManager&gt;()（匿名——守卫工厂直建实现）。</summary>
    public IOrganizationUnitManager Manager => _domainUser.Use<IOrganizationUnitManager>();

    /// <summary>内部接线型 Store——普通 DI 解析（TryAddScoped ImplementationType；ctor IServiceProvider + C1 延迟解析 DataService）。</summary>
    public IOrganizationUnitStore Store => _serviceProvider.GetRequiredService<IOrganizationUnitStore>();

    /// <summary>SG1 DataService（NoAop 路径直建——DataService 禁构造注入，DI004 零豁免；IEntityDAC 从 DI 解析）。</summary>
    public OrganizationUnitEntityDataService OuDataService => _domainUser.Use<OrganizationUnitEntityDataService>();

    /// <summary>SG1 DataService（NoAop 路径直建）。</summary>
    public OrganizationUnitUserEntityDataService UserDataService => _domainUser.Use<OrganizationUnitUserEntityDataService>();

    /// <summary>VEntity 只读 DataService（NoAop 路径直建）。</summary>
    public UserOrganizationUnitViewDataService ViewDataService => _domainUser.Use<UserOrganizationUnitViewDataService>();

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

        // 测试桥接：IDomainUser 注册保留（DataService ctor 需 IDomainUser——生产经 Use<具体类>() NoAop 直建
        // 不经 DI；接线型 Store 的 GetRequiredService<DataService>() 需 DI 可构造）——生产永不注册 IDomainUser（D01）。
        var user = new TestDomainUser();
        services.AddSingleton<IDomainUser>(sp => { user.Provider = sp; return user; });

        // 接线型 Store 的 DataService 解析源：普通 DI 可构造（测试镜像生产 ADR61 可构造工厂，非 throw-factory）；
        // IEntityDAC<T> 基础设施注册同生产 Host（每 DataService 独立 UnitOfWorkManager，对齐 FileManagement 宿主语义）。
        services.AddScoped<IEntityDAC<OrganizationUnitEntity>>(sp => new FreeSqlEntityDAC<OrganizationUnitEntity>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));
        services.AddScoped<IEntityDAC<OrganizationUnitUserEntity>>(sp => new FreeSqlEntityDAC<OrganizationUnitUserEntity>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));
        // V0.2.0 VEntity：UserOrganizationUnitViewDataService 只读 DAC 驱动（显式注册 IEntityReadOnlyDAC 接口——
        // DI 严格按请求类型匹配，IEntityDAC 子接口不自动匹配父接口——02-plan ApprovalExtensionInitializerTests 先例）
        services.AddScoped<IEntityReadOnlyDAC<UserOrganizationUnitView>>(sp => new FreeSqlEntityDAC<UserOrganizationUnitView>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));
        services.AddScoped<OrganizationUnitEntityDataService>();
        services.AddScoped<OrganizationUnitUserEntityDataService>();
        services.AddScoped<UserOrganizationUnitViewDataService>();

        // ITransactionManager（Move/Delete/Create 写路径事务包裹依赖——Noop Begin/Commit 空操作，
        // DataService 逐操作经 UnitOfWorkManager 持久化；Recording 由 configure 覆盖）
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // 扩展初始化器注册 IOrganizationUnitStore（TryAddScoped 接线型）/IOrganizationUnitManager（AddConstructibleService 门面）
        new OrganizationUnitExtensionInitializer<OrganizationUnitUserInfo>().ConfigureServices(services);

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        // 生产路径：绑定当前异步流的解析作用域（DomainHost.NewDomainContext 等价——测试 BindScope）
        DomainUser<TestUserInfo>.BindScope(provider);
        return new OrganizationUnitTestHost(provider, fsql);
    }

    /// <summary>解析服务（Scoped 服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        DomainUser<TestUserInfo>.UnBindScope();
        _serviceProvider.Dispose();
    }
}

/// <summary>Noop 事务管理器——BeginAsync/CommitAsync 空操作（对齐 FileManagement/Calendar 测试宿主）。</summary>
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

/// <summary>
/// 可配置测试用户桩——实现 IDomainUser 最小契约（匿名/认证切换、系统账号、UserId 注入）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价（具体类 DataService——
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>，IDomainUser 参数显式传 this，
/// IEntityDAC 等其余从 DI 解析——对齐 FileManagement/Settings 测试桩）。
/// 本扩展门面/Store 不消费用户上下文（匿名测试路径覆盖生产解析语义），桩主要作 DataService ctor 的
/// DI IDomainUser 解析源。</para>
/// </summary>
internal sealed class TestDomainUser : IDomainUser
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object?> _cache = new();

    /// <summary>ServiceProvider（宿主工厂注入，构建后可用——Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider? Provider { get; set; }

    public string SessionKey { get; set; } = "test-session";

    public bool IsAuthenticated { get; set; } = false;

    public bool IsSystemActor { get; set; } = false;

    public IUserInfo? UserInfo { get; set; }

    public long? TenantId { get; set; }

    public bool IsNoAuditActive { get; set; } = false;

    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public bool IsInRole(string role) => UserInfo?.IsInRole(role) ?? false;

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        var provider = Provider ?? throw new NotSupportedException("Stub: Provider 未注入");

        // 具体类（DataService）：NoAop 等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this——IEntityDAC 从 DI 解析）
        if (_cache.TryGetValue(typeof(TDomainService), out var cached) && cached is TDomainService svc)
            return svc;
        lock (_gate)
        {
            if (_cache.TryGetValue(typeof(TDomainService), out cached) && cached is TDomainService svc2)
                return svc2;
            var resolved = (TDomainService)ActivatorUtilities.CreateInstance(provider, typeof(TDomainService), this);
            _cache[typeof(TDomainService)] = resolved;
            return resolved;
        }
    }

    public TService GetService<TService>() where TService : notnull
        => (Provider ?? throw new NotSupportedException("Stub: Provider 未注入")).GetRequiredService<TService>();

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
