using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Events;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.FeatureManagement;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库创建 + FeatureValue 表结构同步。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;</c> 驱动（对齐 Calendar/OrganizationUnit 测试宿主模式）。</para>
/// </summary>
internal static class FeatureManagementTestSupport
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（每次调用新连接 = 独立内存库，用例隔离）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
        => new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    /// <summary>同步 FeatureValue 表结构。</summary>
    public static void SyncStructure(IFreeSql fsql)
        => fsql.CodeFirst.SyncStructure<FeatureValueEntity>();
}

/// <summary>
/// 完整测试宿主——V4.10.53（领域自治根治，正确路线）重写走<b>生产路径</b>：
/// <list type="bullet">
/// <item><b>真实 DI</b>（Initializer <c>ConfigureServices</c> + FreeSql 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c>；</item>
/// <item><b>门面经 <c>User.Use&lt;接口&gt;()</c> AOP 路径解析</b>（真实 DomainUser → 设 CurrentAopUser → GetRequiredService →
///     AddConstructibleService 守卫工厂 → ActivatorUtilities 直建实现）——<see cref="Manager"/> / <see cref="Store"/>；</item>
/// <item><b>DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 路径直建</b>（ActivatorUtilities + IEntityDAC 从 DI 解析，红线合规）；</item>
/// <item><b>接线型 <see cref="IFeatureChecker"/> 普通 DI 解析</b>（GetRequiredService——框架 FeatureFilterAttribute 同路径；
///     ctor(IServiceProvider, ILogger)，内部 C1 延迟解析 IFeatureManager——测试经 ambient 桩设 CurrentAopUser 通过守卫工厂）；</item>
/// <item><b>IDomainUser 注册保留（T3 桥接）</b>：内置四 Provider（User/Role/Tenant/Global，边界保留组）ctor(IDomainUser)——
///     多实现集合经普通 DI GetServices 构造需 IDomainUser 可解析（框架机制缺口 T3 转达候选，本批保留）——测试注册
///     <see cref="TestDomainUser"/> 桩（生产永不注册 IDomainUser——D01）；</item>
/// <item><b>Stub 桩 <c>Use&lt;T&gt;()</c> 按生产等价</b>：接口 → 设 CurrentAopUser 后 GetRequiredService（AOP 等价）；
///     具体类 → ActivatorUtilities 直建（NoAop 等价）。</item>
/// </list>
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库实例（用例隔离）。
/// <c>configure</c> 回调在扩展初始化器之后执行——可覆盖 ITransactionManager/FeatureOptions
/// （如缓存 TTL 缩短测试：<c>services.Configure&lt;FeatureOptions&gt;(o =&gt; o.CacheExpirationSeconds = 1)</c>）。</para>
/// <para><b>贡献者收集</b>：初始化的 <see cref="FeatureManagementExtensionInitializer{TUserInfo}.ConfigureServices"/>
/// 从 <c>ProjectMetaContextBase.Instance.Contributors["Feature"]</c> 读取编译期清单（v4.10.31 A+ 阶段 3 起新桥）——测试经
/// <see cref="FeatureTestMetaContext"/> 安装（镜像 ConsumerIntegrationTests FakeMetaContext 模式），
/// 使 DI 中 <c>IFeatureDefinitionRepository</c> 含 <see cref="ConsumerFeatureContributor"/> 声明定义。
/// 静态单例换装以锁串行化，避免并行用例交叉污染。</para>
/// </summary>
internal sealed class FeatureManagementTestHost : IDisposable
{
    /// <summary>串行化 ProjectMetaContextBase.Instance 换装（进程级静态，防并行用例交叉）。</summary>
    private static readonly object s_metaContextLock = new();

    private readonly ServiceProvider _serviceProvider;

    /// <summary>生产路径解析用户（真实 DomainUser&lt;TestUserInfo&gt;，匿名——Manager/Store/DataService 经 Use&lt;T&gt;() 解析）。</summary>
    private readonly DomainUser<TestUserInfo> _domainUser = new();

    public IFreeSql Fsql { get; }

    /// <summary>可配置用户桩——测试内可切换认证状态/UserId/TenantId/Roles（匿名 vs 认证）；作门面方法的显式 user 参数 + ambient 桩。</summary>
    public TestDomainUser User { get; }

    /// <summary>门面——生产 AOP 路径：真实 DomainUser.Use&lt;IFeatureManager&gt;()（守卫工厂直建）。</summary>
    public IFeatureManager Manager => _domainUser.Use<IFeatureManager>();

    /// <summary>接线型——普通 DI 解析（框架 FeatureFilterAttribute GetService 同路径；ctor IServiceProvider + C1 延迟解析 Manager）。</summary>
    public IFeatureChecker Checker => _serviceProvider.GetRequiredService<IFeatureChecker>();

    /// <summary>Feature 值存储（v0.3.0 public 契约——消费方自定义 Provider 可注入；实现类仍 internal，DI 经扩展注册——守卫工厂）。</summary>
    public IFeatureValueStore Store => _domainUser.Use<IFeatureValueStore>();

    public IFeatureDefinitionRepository DefinitionRepository => _serviceProvider.GetRequiredService<IFeatureDefinitionRepository>();

    /// <summary>SG1 DataService（存储层——管理 API 写路径经 Manager 委托，禁止裸 CRUD 直通；NoAop 路径直建）。</summary>
    public FeatureValueEntityDataService DataService => _domainUser.Use<FeatureValueEntityDataService>();

    /// <summary>缓存版本表（v0.2.0/0.3.0——跨实例失效测试直读版本断言）。</summary>
    public FeatureCacheVersionRegistry VersionRegistry => _serviceProvider.GetRequiredService<FeatureCacheVersionRegistry>();

    /// <summary>分布式事件总线（v0.3.0——默认 LocalDistributedEventBus 进程内降级；无总线模式验证用）。</summary>
    public IDistributedEventBus DistributedBus => _serviceProvider.GetRequiredService<IDistributedEventBus>();

    /// <summary>扩展内建跨实例失效 handler（v0.3.0——测试项目无 SG4 扫描，手动 AddTransient；D9 经总线订阅验证 LocalHandlerAdapter）。</summary>
    public DistributedFeatureChangedHandler DistributedFeatureChangedHandler => _serviceProvider.GetRequiredService<DistributedFeatureChangedHandler>();

    private FeatureManagementTestHost(ServiceProvider serviceProvider, IFreeSql fsql, TestDomainUser user)
    {
        _serviceProvider = serviceProvider;
        Fsql = fsql;
        User = user;
    }

    /// <summary>全新宿主（每次调用独立 SQLite 内存库 + 独立 MemoryCache；BindScope 绑定当前异步流）。</summary>
    public static FeatureManagementTestHost Create(Action<IServiceCollection>? configure = null)
    {
        var fsql = FeatureManagementTestSupport.CreateInMemoryFreeSql();
        FeatureManagementTestSupport.SyncStructure(fsql);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);
        // IConfiguration——AddOptions<FeatureOptions>().BindConfiguration("TKWF:FeatureManagement") 依赖（空配置，默认值生效）
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        // V4.10.55（ADR92）：Provider 守卫工厂直接从 CurrentAopUser 供给（不再解析 DI 的 IDomainUser 注册——
        // SetCurrentUser/host.User 桩直传），DI 桩注册不再触发 → 须显式注入 Provider（内部 User.Use<T>() 解析源）。
        // 桩仍保留：显式 user 参数（匿名/认证切换）+ ambient 守卫（DomainUserContext.CurrentAopUser 非空）。
        var user = new TestDomainUser();
        services.AddSingleton<IDomainUser>(sp => { user.Provider = sp; return user; });

        // FreeSql 基础设施（消费方 DomainHost 等价注册）——DataService 不手动注册（生产经 SG 消费方聚合自动注册，
        // 测试经 Use<具体类>() NoAop 路径直建，IEntityDAC 从 DI 解析）
        services.AddScoped<UnitOfWorkManager>();
        services.AddScoped<IEntityDAC<FeatureValueEntity>>(sp => new FreeSqlEntityDAC<FeatureValueEntity>(sp.GetRequiredService<UnitOfWorkManager>()));

        // ITransactionManager（写路径事务包裹依赖——Noop Begin/Commit 空操作，
        // DataService 逐操作经 UnitOfWorkManager 持久化；对齐 Calendar/OrganizationUnit 宿主共识）
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // IMemoryCache 真实实例（TryAddSingleton：扩展默认注册不覆盖消费方实例）
        services.TryAddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));

        // v0.2.0：ILocalEventBus（变更事件发布依赖——主框架 LocalEventBus 进程内实现）
        services.AddSingleton<TKW.Framework.Domain.Events.ILocalEventBus, TKW.Framework.Domain.Events.LocalEventBus>();

        // v0.3.0：IDistributedEventBus（默认进程内降级——LocalDistributedEventBus 委托 ILocalEventBus；
        // 未接 RabbitMQ 时 [DistributedEvent] 事件静默走本地，行为与 v0.2.0 一致，F8）
        services.AddSingleton<TKW.Framework.Domain.Events.IDistributedEventBus, TKW.Framework.Domain.Events.LocalDistributedEventBus>();
        // v0.3.0：跨实例失效 handler 手动注册（测试项目无消费方 SG4 扫描——扩展 Initializer 不手动注册）
        services.AddTransient<DistributedFeatureChangedHandler>();

        // 贡献者收集 + 扩展初始化器注册（TryAddScoped 不覆盖已注册 IMemoryCache；Store/Manager 经 AddConstructibleService）
        lock (s_metaContextLock)
        {
            var original = ProjectMetaContextBase.Instance;
            try
            {
                FeatureTestMetaContext.Install();
                new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureServices(services);
            }
            finally
            {
                FeatureTestMetaContext.Restore(original);
            }
        }

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        // V4.10.55（ADR92）：守卫工厂从 CurrentAopUser 直接供给 user——DI 的 IDomainUser 注册不会被解析触发，
        // 桩 Provider 须在此显式注入（内部 FeatureManager.User.Use<T>() 经此解析；对齐 MFA/Notifications 宿主模式）
        user.Provider = provider;
        // 生产路径：绑定当前异步流的解析作用域（DomainHost.NewDomainContext 等价——测试 BindScope）
        DomainUser<TestUserInfo>.BindScope(provider);
        return new FeatureManagementTestHost(provider, fsql, user);
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

/// <summary>
/// 测试用 ProjectMetaContext——override <c>Contributors["Feature"]</c> 返回 <see cref="ConsumerFeatureContributor"/> 清单。
/// <para>V4.10.31 (A+ 阶段 3)：由 override <c>FeatureContributors</c>（旧桥，已 Obsolete）改为 override
/// <c>Contributors[ContributorTargetKinds.Feature]</c>（统一描述符字典，对齐 Navigation/Permissions 测试）。
/// <c>ProjectMetaContextBase.Instance</c> setter 是 protected，经子类公开 Install/Restore。
/// 静态隔离约束（镜像 Permissions/Navigation 测试）：进程级单例，try/finally 恢复 + 宿主锁串行化。</para>
/// </summary>
internal sealed class FeatureTestMetaContext : ProjectMetaContextBase
{
    public static void Install() => Instance = new FeatureTestMetaContext();

    public static void Restore(IProjectMetaContext? original) => Instance = original;

    public override IReadOnlyDictionary<string, IReadOnlyList<ContributorDescriptor>> Contributors =>
        new Dictionary<string, IReadOnlyList<ContributorDescriptor>>
        {
            [ContributorTargetKinds.Feature] = new[]
            {
                new ContributorDescriptor(
                    "TKWF.Ext.FeatureManagement.Tests.ConsumerFeatureContributor",
                    "ConsumerFeatureContributor",
                    typeof(ConsumerFeatureContributor),
                    ContributorTargetKinds.Feature)
            }
        };

    // V4.10.32 (A+ 阶段 4)：编译期实例化 override——测试桩镜像 Initializer 消费路径（CreateContributorInstances）
    public override IReadOnlyList<object> CreateContributorInstances(string targetKind)
        => targetKind == ContributorTargetKinds.Feature
            ? new object[] { new ConsumerFeatureContributor() }
            : Array.Empty<object>();

    public override ProjectConfiguration Configuration => null!;
    public override MetadataChangeLog ChangeLog => null!;
    public override string MetadataSchemaVersion => "test";
}

/// <summary>Noop 事务管理器——BeginAsync/CommitAsync 空操作（对齐 Calendar/OrganizationUnit 测试宿主）。</summary>
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
/// 可配置测试用户桩——实现 IDomainUser 最小契约（匿名/认证切换、租户、角色注入）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产路径等价实现——接口（IDomainService）经
/// 设 <c>DomainUserContext.CurrentAopUser</c> 后 GetRequiredService（AOP 等价——AddConstructibleService 守卫工厂
/// 需 CurrentAopUser 非空）；具体类（DataService）经 <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>
/// 直建（IDomainUser 参数显式传 this，IEntityDAC 等其余从 DI 解析——NoAop 等价，对齐 Settings/Tagging 测试桩）。</para>
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

    /// <summary>快捷配置：认证用户（UserId/TenantId/角色列表一步就位）。</summary>
    public TestDomainUser AsAuthenticated(string userId, long? tenantId = null, params string[] roles)
    {
        IsAuthenticated = true;
        UserId = userId;
        TenantId = tenantId;
        UserName = userId;
        UserInfo = new TestUserInfo(userId, userId, roles);
        return this;
    }

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        var provider = Provider ?? throw new NotSupportedException("Stub: Provider 未注入");

        // 接口（IDomainService）：AOP 等价——设 CurrentAopUser 后 GetRequiredService（AddConstructibleService 守卫工厂需非空）
        if (typeof(TDomainService).IsInterface)
        {
            var previous = DomainUserContext.CurrentAopUser;
            DomainUserContext.CurrentAopUser = this;
            try
            {
                return provider.GetRequiredService<TDomainService>();
            }
            finally
            {
                DomainUserContext.CurrentAopUser = previous;
            }
        }

        // 具体类：NoAop 等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this——IEntityDAC 从 DI 解析）
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

