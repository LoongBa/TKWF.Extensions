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
using TKW.Framework.Domain.Events;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库创建 + FeatureValue 表结构同步。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;</c> 驱动（对齐 Calendar/OrganizationUnit 测试宿主模式）；
/// v4.10.8 (ADR61) 起 DataService 经测试版可构造工厂（DI 兜底，镜像生产 AddConstructibleDataService）注册。</para>
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
/// 完整测试宿主——构建 DI 容器：DataService（v4.10.8 ADR61 DI 兜底工厂注册：
/// ActivatorUtilities.CreateInstance + DI IDomainUser）+ ITransactionManager（Noop）+ IMemoryCache 真实实例 + FeatureOptions。
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库实例（用例隔离）。
/// <paramref name="configure"/> 回调在扩展初始化器之后执行——可覆盖 ITransactionManager/FeatureOptions
/// （如缓存 TTL 缩短测试：<c>services.Configure&lt;FeatureOptions&gt;(o =&gt; o.CacheExpirationSeconds = 1)</c>）。</para>
/// <para><b>贡献者收集</b>：初始化的 <see cref="FeatureManagementExtensionInitializer{TUserInfo}.ConfigureServices"/>
/// 从 <c>ProjectMetaContextBase.Instance.FeatureContributors</c> 读取编译期清单——测试经
/// <see cref="FeatureTestMetaContext"/> 安装（镜像 ConsumerIntegrationTests FakeMetaContext 模式），
/// 使 DI 中 <c>IFeatureDefinitionRepository</c> 含 <see cref="ConsumerFeatureContributor"/> 声明定义。
/// 静态单例换装以锁串行化，避免并行用例交叉污染。</para>
/// </summary>
internal sealed class FeatureManagementTestHost : IDisposable
{
    /// <summary>串行化 ProjectMetaContextBase.Instance 换装（进程级静态，防并行用例交叉）。</summary>
    private static readonly object s_metaContextLock = new();

    private readonly ServiceProvider _serviceProvider;

    public IFreeSql Fsql { get; }

    /// <summary>可配置用户桩——测试内可切换认证状态/UserId/TenantId/Roles（匿名 vs 认证）。</summary>
    public TestDomainUser User { get; }

    public IFeatureManager Manager => _serviceProvider.GetRequiredService<IFeatureManager>();

    public IFeatureChecker Checker => _serviceProvider.GetRequiredService<IFeatureChecker>();

    /// <summary>Feature 值存储（v0.3.0 public 契约——消费方自定义 Provider 可注入；实现类仍 internal，DI 经扩展注册）。</summary>
    public IFeatureValueStore Store => _serviceProvider.GetRequiredService<IFeatureValueStore>();

    public IFeatureDefinitionRepository DefinitionRepository => _serviceProvider.GetRequiredService<IFeatureDefinitionRepository>();

    /// <summary>SG1 DataService（存储层——管理 API 写路径经 Manager 委托，禁止裸 CRUD 直通）。</summary>
    public FeatureValueEntityDataService DataService => _serviceProvider.GetRequiredService<FeatureValueEntityDataService>();

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

    /// <summary>全新宿主（每次调用独立 SQLite 内存库 + 独立 MemoryCache）。</summary>
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

        var user = new TestDomainUser();
        services.AddSingleton<IDomainUser>(user);

        // v4.10.8 (ADR61) 迁移：模拟生产 DataService 自动注册——测试容器不走消费方 SG 聚合，
        // 用与生产同构的 DI 兜底工厂（镜像 AddConstructibleDataService：ActivatorUtilities.CreateInstance
        // + 域用户；测试用户源 = DI IDomainUser 而非 AsyncLocal CurrentAopUser——免域作用域，xUnit 并行安全）。
        services.AddScoped<UnitOfWorkManager>();
        services.AddScoped<IEntityDAC<FeatureValueEntity>>(sp => new FreeSqlEntityDAC<FeatureValueEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        AddTestConstructibleDataService<FeatureValueEntityDataService>(services);

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

        // 贡献者收集 + 扩展初始化器注册（TryAddScoped 不覆盖已注册 DataService/IMemoryCache）
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

        return new FeatureManagementTestHost(services.BuildServiceProvider(), fsql, user);
    }

    /// <summary>v4.10.8 (ADR61) 迁移：测试版可构造 DataService 工厂——镜像生产
    /// <c>AddConstructibleDataService</c>（<c>ActivatorUtilities.CreateInstance</c> + 域用户），
    /// 用户源改为 DI <c>IDomainUser</c>（TestDomainUser）而非 AsyncLocal <c>CurrentAopUser</c>——
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

/// <summary>
/// 测试用 ProjectMetaContext——override <c>FeatureContributors</c> 返回 <see cref="ConsumerFeatureContributor"/> 清单。
/// <para>主框架 C1 改动已落地：<c>ProjectMetaContextBase.FeatureContributors</c>（virtual，仅基类不入接口——ADR D2）。
/// <c>ProjectMetaContextBase.Instance</c> setter 是 protected，经子类公开 Install/Restore。
/// 静态隔离约束（镜像 Permissions/Navigation 测试）：进程级单例，try/finally 恢复 + 宿主锁串行化。</para>
/// </summary>
internal sealed class FeatureTestMetaContext : ProjectMetaContextBase
{
    public static void Install() => Instance = new FeatureTestMetaContext();

    public static void Restore(IProjectMetaContext? original) => Instance = original;

    public override IReadOnlyList<PermissionContributorData> FeatureContributors =>
        new[]
        {
            new PermissionContributorData(
                "TKWF.Ext.FeatureManagement.Tests.ConsumerFeatureContributor",
                "ConsumerFeatureContributor",
                typeof(ConsumerFeatureContributor))
        };

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
/// <para><see cref="UserInfo"/> 用可配置 <see cref="TestUserInfo"/>（Roles 列表注入——Role 层遍历序测试用）。</para>
/// </summary>
internal sealed class TestDomainUser : IDomainUser
{
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
        => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");

    public TService GetService<TService>() where TService : notnull
        => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}

/// <summary>
/// 消费方最小用户类型——模拟真实消费方定义自己的 UserInfo。
/// <para>Role 层测试要点：<see cref="SimpleUserInfo.Roles"/> 为可 set 属性——
/// 既支持构造器注入（<c>new TestUserInfo("u-1", "u-1", "role-a", "role-b")</c>）也支持集合初始化
/// （<c>new TestUserInfo { Roles = ["role-a"] }</c>）。</para>
/// </summary>
public class TestUserInfo : SimpleUserInfo
{
    public TestUserInfo() : base() { }

    public TestUserInfo(string userIdString, string userName, params string[] roles)
        : base(userIdString, userName)
    {
        Roles = roles.ToList();
    }
}
