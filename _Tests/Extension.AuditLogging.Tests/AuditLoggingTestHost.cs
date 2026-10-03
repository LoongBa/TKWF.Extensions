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
/// 测试公共设施（V4.10.53 领域自治根治后重写——生产路径等价）。
/// <para>弃旧 StubDomainUser + 手写 DataService 单例注册（掩盖 IDomainUser 生产解析失败）；
/// 改对齐 Settings/Account 整改后宿主：</para>
/// <list type="bullet">
/// <item><b>接线型 Store</b>（<see cref="AuditLogStore"/>，主框架契约非 IDomainService——不可修改主框架）——
///     真实 DI 解析链：ctor(IServiceProvider, ILogger) 直构 + <c>sp.GetRequiredService&lt;AuditLogEntityDataService&gt;()</c>
///     C1 延迟解析（DataService 在 provider 中可构造注册）；</item>
/// <item><b>标准门面</b>（<see cref="AuditLogQueryService"/> / <see cref="AuditLogAnalyticsService"/>，继承 DomainServiceBase）——
///     可配置 stub 直构（经基类 User 取上下文），其 <c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价
///     （<c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>，IEntityDAC 从 DI 解析）。</item>
/// </list>
/// </summary>
internal static class AuditLoggingTestHost
{
    /// <summary>构建 SQLite 内存 + 真实 FreeSql 基础设施 provider（IFreeSql/UnitOfWorkManager/IEntityDAC）——
    /// stub <c>Use&lt;T&gt;()</c> NoAop 直建 DataService 的解析源（对齐 Settings/Account 测试桩）。</summary>
    public static IServiceProvider BuildInfrastructure(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<AuditLogEntity>, FreeSqlEntityDAC<AuditLogEntity>>();
        return services.BuildServiceProvider();
    }

    /// <summary>构建基于 SQLite 内存库的可配置 stub 用户（经基类 User 取上下文）。</summary>
    public static StubDomainUser BuildStub(IFreeSql fsql)
        => new() { ServiceProvider = BuildInfrastructure(fsql) };

    /// <summary>构建接线型 Store 解析链 provider——DataService 经普通 DI 可构造解析
    /// （<c>GetRequiredService&lt;AuditLogEntityDataService&gt;()</c> 需 IDomainUser + IEntityDAC 可解析；
    /// 对齐生产 DataService 消费方聚合可构造工厂形态）。</summary>
    private static ServiceProvider BuildWiringProvider(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<AuditLogEntity>, FreeSqlEntityDAC<AuditLogEntity>>();
        // ⚠️ 测试内部接线桩：接线型 Store 的 DataService C1 延迟解析链需要 IDomainUser 可解析（测试模拟消费方 DataService
        // 聚合可构造工厂形态——DataService ctor 有 IDomainUser 参数）；生产环境 DataService 经 User.Use<具体类>() NoAop
        // 直建不注册 IDomainUser（D01 铁律）。此桩仅测试宿主，非生产注册（T3 候选：DataService 可构造工厂形态待框架统一）。
        services.AddScoped<IDomainUser>(_ => new StubDomainUser());
        services.AddScoped<AuditLogEntityDataService>();
        return services.BuildServiceProvider();
    }

    /// <summary>创建基于 SQLite 内存库的 AuditLogStore（接线型——真实 DI 解析链，DataService 经 SP 延迟解析）。</summary>
    public static AuditLogStore CreateStore(IFreeSql fsql)
    {
        var sp = BuildWiringProvider(fsql);
        return new AuditLogStore(sp, NullLogger<AuditLogStore>.Instance);
    }

    /// <summary>创建基于 SQLite 内存库的 AuditLogQueryService（标准门面——经基类 User 懒加载 DataService）。</summary>
    public static AuditLogQueryService CreateQueryService(IFreeSql fsql)
    {
        var user = BuildStub(fsql);
        return new AuditLogQueryService(user, NullLogger<AuditLogQueryService>.Instance);
    }

    /// <summary>创建基于 SQLite 内存库的 AuditLogAnalyticsService（标准门面——经基类 User 懒加载 DataService + Options + 空日志）。</summary>
    public static AuditLogAnalyticsService CreateAnalyticsService(
        IFreeSql fsql, AuditLoggingOptions? options = null)
    {
        var user = BuildStub(fsql);
        return new AuditLogAnalyticsService(user,
            new OptionsWrapper<AuditLoggingOptions>(options ?? new AuditLoggingOptions()),
            NullLogger<AuditLogAnalyticsService>.Instance);
    }

    /// <summary>创建基于 SQLite 内存库的 AuditLogEntityDataService（真实 FreeSql DAC 驱动——管理 API/落库断言直构用）。</summary>
    public static AuditLogEntityDataService CreateDataService(IFreeSql fsql, IDomainUser? user = null)
    {
        var dac = new FreeSqlEntityDAC<AuditLogEntity>(new UnitOfWorkManager(fsql));
        return new AuditLogEntityDataService(user ?? new StubDomainUser(), dac);
    }
}

/// <summary>
/// 测试用户桩——实现 <see cref="IDomainUser"/> 最小契约（User/Tenant/匿名可配置）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价实现——具体类经
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c> 直建（IDomainUser 参数显式传 this，
/// IEntityDAC 等其余从 DI 解析——对齐 Settings/Account 测试桩）。</para>
/// </summary>
internal sealed class StubDomainUser : IDomainUser
{
    private readonly string? _userId;
    private readonly long? _tenantId;
    private readonly bool _isAuthenticated;

    public StubDomainUser(string? userId = null, long? tenantId = null, bool isAuthenticated = false)
    {
        _userId = userId;
        _tenantId = tenantId;
        _isAuthenticated = isAuthenticated;
    }

    /// <summary>ServiceProvider（测试工厂注册时注入——Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider? ServiceProvider { get; set; }

    public string SessionKey => "test-session";
    public bool IsAuthenticated => _isAuthenticated;
    public bool IsSystemActor => false;
    public IUserInfo? UserInfo => null;
    public long? TenantId => _tenantId;
    public bool IsNoAuditActive => false;
    public string? UserId => _userId;
    public string? UserName => "test";
    public bool IsInRole(string role) => false;

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        if (ServiceProvider is null)
            throw new NotSupportedException("Stub: Use<T> 未注入 ServiceProvider");

        // 生产 NoAop 路径等价（具体类）：ActivatorUtilities 直建，IDomainUser 参数显式传 this
        return (TDomainService)ActivatorUtilities.CreateInstance(ServiceProvider, typeof(TDomainService), this);
    }

    public TService GetService<TService>() where TService : notnull
    {
        if (ServiceProvider is null)
            throw new NotSupportedException("Stub: GetService<T> 未注入 ServiceProvider");
        return ServiceProvider.GetRequiredService<TService>();
    }

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
