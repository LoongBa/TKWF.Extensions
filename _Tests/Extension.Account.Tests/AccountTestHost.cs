using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// 测试公共设施——分层单测宿主（V4.10.53 领域自治根治后重写）。
/// <para>生产路径等价：Store 现继承 <see cref="TKW.Framework.Domain.DomainServiceBase"/>，DataService 经基类
/// <c>User</c> 懒加载（NoAop 路径）——测试用可配置 <see cref="StubDomainUser"/> 直构 Store（经基类 User 取上下文），
/// 其 <c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价实现（<c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>，
/// IEntityDAC 从 DI 解析——与 Settings/Tagging 测试同模式）。</para>
/// <para>集成测试（真实 DI + BindScope + User.Use&lt;接口&gt;()）见 <see cref="AccountProductionPathTests"/>。</para>
/// </summary>
internal static class AccountTestHost
{
    /// <summary>构建基于 SQLite 内存库 + 真实 FreeSqlEntityDAC 的 AccountLockoutStore（分层单测用）。</summary>
    public static AccountLockoutStore CreateLockoutStore(IFreeSql fsql)
    {
        var user = BuildStub(fsql, typeof(AccountLockoutEntity));
        return new AccountLockoutStore(user, NullLogger<AccountLockoutStore>.Instance);
    }

    /// <summary>构建基于 SQLite 内存库 + 真实 FreeSqlEntityDAC 的 PasswordResetStore（分层单测用）。</summary>
    public static PasswordResetStore CreatePasswordResetStore(IFreeSql fsql)
    {
        var user = BuildStub(fsql, typeof(PasswordResetCodeEntity));
        return new PasswordResetStore(user, NullLogger<PasswordResetStore>.Instance);
    }

    private static StubDomainUser BuildStub(IFreeSql fsql, params Type[] entityTypes)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        foreach (var entityType in entityTypes)
        {
            var dacType = typeof(IEntityDAC<>).MakeGenericType(entityType);
            var implType = typeof(FreeSqlEntityDAC<>).MakeGenericType(entityType);
            services.AddSingleton(dacType, implType);
        }
        var provider = services.BuildServiceProvider();
        return new StubDomainUser { ServiceProvider = provider };
    }
}

/// <summary>
/// 测试用户桩——实现 <see cref="IDomainUser"/> 最小契约（User/Tenant/匿名可配置）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价实现——具体类经
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c> 直建（IDomainUser 参数显式传 this，
/// IEntityDAC 等其余从 DI 解析——对齐 Settings/Tagging 测试桩）；接口（IDomainService）经
/// <c>provider.GetRequiredService(t)</c> 解析（守卫工厂在集成测试经真实 DomainUser 验证，桩内不重复守卫）。</para>
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

    /// <summary>ServiceProvider（测试工厂注册时注入——User.Use&lt;T&gt;() 解析源）。</summary>
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