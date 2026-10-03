using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// 测试公共设施——V4.10.53（领域自治根治，ADR90）重写：宿主走<b>生产路径</b>。
/// <list type="bullet">
/// <item><strong>集成测试</strong>：真实 DI（Initializer <c>ConfigureServices</c> + FreeSql 基础设施 + <c>AddLogging</c>）
///     → <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> → <c>User.Use&lt;ISecurityLogStore&gt;()</c> 等 AOP 路径
///     （设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂 → ActivatorUtilities 直建实现）；</item>
/// <item><strong>故障/单测</strong>：可配置 stub 用户（<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价——
///     <c>ActivatorUtilities.CreateInstance</c>，IDomainUser 参数显式传 this，其余从注入的 ServiceProvider 解析）。</item>
/// </list>
/// <para>DataService 不手动注册——生产经 SG 消费方聚合自动注册（throw-factory）；
/// 测试经 <c>User.Use&lt;SecurityLogEntityDataService&gt;()</c> NoAop 路径直建（<c>IEntityDAC&lt;SecurityLogEntity&gt;</c> 从 DI 解析，
/// 数据访问红线合规——零裸 ORM/IEntityDAC 直注入）。</para>
/// </summary>
internal static class SecurityLogTestHost
{
    /// <summary>创建 SQLite :memory: 库 + 同步实体表结构。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<SecurityLogEntity>();
        return fsql;
    }

    /// <summary>
    /// 构建消费方生产形态 DI 宿主（对齐 Settings V0.3.0 / Account V4.10.53 测试宿主范式）。
    /// DataService 不手动注册——生产经 SG 消费方聚合自动注册（throw-factory）；
    /// 测试经 <c>Use&lt;SecurityLogEntityDataService&gt;()</c> NoAop 路径直建（IEntityDAC 从 DI 解析，红线合规）。
    /// </summary>
    /// <param name="fsql">SQLite 内存库。</param>
    /// <param name="configureOptions">可选：覆盖 <see cref="SecurityLoggingOptions"/>（如 CleanupBatchSize 分批测试）。</param>
    public static (ServiceProvider Provider, DomainUser<TestUserInfo> User) CreateProductionHost(
        IFreeSql fsql, Action<SecurityLoggingOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // BindConfiguration 惰性读取 IConfiguration——注册空配置桩（对齐 Tagging 测试先例）
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // 扩展装配——生产形态（消费方白名单启用后三钩子执行 ConfigureServices）
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        if (configureOptions != null)
            services.Configure<SecurityLoggingOptions>(configureOptions);

        // FreeSql 基础设施（消费方 DomainHost 等价注册）
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<SecurityLogEntity>, FreeSqlEntityDAC<SecurityLogEntity>>();

        var provider = services.BuildServiceProvider();
        // 生产经 DomainHost.NewDomainContext 绑定 AsyncLocal——测试等价 BindScope
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        return (provider, user);
    }

    /// <summary>构建故障/直构单测 stub——Use&lt;T&gt;() NoAop 路径等价（ActivatorUtilities 直建 + DI 解析 IEntityDAC）。</summary>
    public static StubDomainUser CreateStub(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<SecurityLogEntity>, FreeSqlEntityDAC<SecurityLogEntity>>();
        var stub = new StubDomainUser();
        stub.ServiceProvider = services.BuildServiceProvider();
        return stub;
    }

    /// <summary>直构 Store（stub + 空日志）——供过滤器端到端测试经 ctx.Store 注入管线（分层单测形态）。</summary>
    public static SecurityLogStore CreateStore(IFreeSql fsql)
        => new(CreateStub(fsql), Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityLogStore>.Instance);

    /// <summary>直构 QueryService（stub + 空日志）——供过滤器端到端测试回查（分层单测形态）。</summary>
    public static SecurityLogQueryService CreateQueryService(IFreeSql fsql)
        => new(CreateStub(fsql), Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityLogQueryService>.Instance);
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。
/// <para>V4.10.53（领域自治根治）：Use&lt;T&gt;() 按生产 NoAop 路径等价——<c>ActivatorUtilities.CreateInstance</c>
/// （IDomainUser 参数显式传 this，其余参数从注入的 ServiceProvider 解析——对齐 Settings/Account 整改后测试宿主）。</para></summary>
internal sealed class StubDomainUser : IDomainUser
{
    private IServiceProvider? _provider;

    /// <summary>ServiceProvider（测试工厂注册时注入——懒加载 Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider? ServiceProvider { set => _provider = value; get => _provider; }

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
        // 生产 NoAop 路径等价：ActivatorUtilities 直建，IDomainUser 参数显式传 this，其余从 DI 解析
        var provider = _provider ?? throw new NotSupportedException("Stub: Use<T> 未注入 ServiceProvider");
        return (TDomainService)ActivatorUtilities.CreateInstance(provider, typeof(TDomainService), this);
    }

    public TService GetService<TService>() where TService : notnull
    {
        var provider = _provider ?? throw new NotSupportedException("Stub: GetService<T> 未注入 ServiceProvider");
        return provider.GetRequiredService<TService>();
    }

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
