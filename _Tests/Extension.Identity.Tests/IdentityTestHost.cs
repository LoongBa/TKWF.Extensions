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

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// 测试公共设施——分层单测宿主（V4.10.53 领域自治根治后重写）。
/// <para>生产路径等价：Store/Manager 现继承 <see cref="TKW.Framework.Domain.DomainServiceBase"/>，DataService 经基类
/// <c>User</c> 懒加载（NoAop 路径）——测试用可配置 <see cref="StubDomainUser"/> 直构门面（经基类 User 取上下文），
/// 其 <c>Use&lt;T&gt;()</c> 按生产路径等价实现：具体类经 <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>
/// 直建（IEntityDAC 从 DI 解析），接口（IDomainService）经 <c>provider.GetRequiredService(t)</c> 解析
/// （对齐 Settings/Tagging 测试桩；守卫工厂在集成测试经真实 DomainUser 验证，桩内不重复守卫）。</para>
/// <para>集成测试（真实 DI + BindScope + User.Use&lt;接口&gt;()）见 <see cref="IdentityProductionPathTests"/>。</para>
/// </summary>
internal static class IdentityTestHost
{
    /// <summary>建表 + 建 VEntity 视图（SQLite 方言）——测试公共设施统一入口。</summary>
    public static void SyncSchema(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<UserEntity>();
        fsql.CodeFirst.SyncStructure<RoleEntity>();
        fsql.CodeFirst.SyncStructure<UserRoleEntity>();
        // V0.2.0 VEntity：建真实视图（SQLite 方言，来自 UserRoleView.ViewSqlSQLite）——不跑宿主 SyncViewsAsync。
        // 注意：SQLite 表无 CreateTime/UpdateTime 列（FreeSql SQLite provider 不支持 DateTimeOffset），视图用 NULL 占位对齐列
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""vw_UserRoleView"" AS
SELECT ur.""UserId"", r.""Id"", r.""Name"", r.""DisplayName"", r.""IsSystemRole"",
       NULL AS ""CreateTime"", NULL AS ""UpdateTime""
FROM ""IdentityUserRole"" ur
INNER JOIN ""IdentityRole"" r ON ur.""RoleId"" = r.""Id""");
    }

    /// <summary>构建基于 SQLite 内存库 + 真实 FreeSqlEntityDAC 的 UserStore（3 DataService 委托 + VEntity 只读 DataService）。</summary>
    public static UserStore CreateUserStore(IFreeSql fsql)
    {
        var user = BuildStub(fsql, [typeof(UserEntity), typeof(UserRoleEntity), typeof(UserRoleView)]);
        return new UserStore(user, NullLogger<UserStore>.Instance);
    }

    /// <summary>构建基于 SQLite 内存库 + 真实 FreeSqlEntityDAC 的 RoleStore（2 DataService 委托）。</summary>
    public static RoleStore CreateRoleStore(IFreeSql fsql)
    {
        var user = BuildStub(fsql, [typeof(RoleEntity), typeof(UserRoleEntity)]);
        return new RoleStore(user, NullLogger<RoleStore>.Instance);
    }

    /// <summary>创建 UserManager（UserStore + RoleStore 组合，V0.3.0 测试用）——经基类 User.Use&lt;IUserStore&gt;() 懒加载。</summary>
    public static IUserManager CreateUserManager(IFreeSql fsql, IdentityOptions? options = null)
    {
        var user = BuildStub(fsql,
            [typeof(UserEntity), typeof(UserRoleEntity), typeof(UserRoleView), typeof(RoleEntity)],
            (s, u) =>
            {
                s.AddSingleton<IUserStore>(new UserStore(u, NullLogger<UserStore>.Instance));
                s.AddSingleton<IRoleStore>(new RoleStore(u, NullLogger<RoleStore>.Instance));
            });
        return new UserManager(user, Options.Create(options ?? new IdentityOptions()), NullLogger<UserManager>.Instance);
    }

    /// <summary>创建 IdentityAuthService（V0.3.0 注册/登录测试用）——经基类 User.Use&lt;IUserManager&gt;() 懒加载。</summary>
    public static IdentityAuthService CreateAuthService(IFreeSql fsql)
    {
        var user = BuildStub(fsql,
            [typeof(UserEntity), typeof(UserRoleEntity), typeof(UserRoleView), typeof(RoleEntity)],
            (s, u) =>
            {
                s.AddSingleton<IUserStore>(new UserStore(u, NullLogger<UserStore>.Instance));
                s.AddSingleton<IRoleStore>(new RoleStore(u, NullLogger<RoleStore>.Instance));
                s.AddSingleton<IUserManager>(new UserManager(u, Options.Create(new IdentityOptions()), NullLogger<UserManager>.Instance));
            });
        return new IdentityAuthService(user);
    }

    /// <summary>构建解析 IUserManager 的 ServiceProvider（IdentityPasswordManager/IdentityRoleProvider 接线型直构用——
    /// ctor(IServiceProvider) + GetRequiredService 延迟解析）。</summary>
    public static IServiceProvider CreateProviderForManager(IUserManager manager)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserManager>(manager);
        return services.BuildServiceProvider();
    }

    /// <summary>创建 UserRoleViewQueryService（V0.4.0 REST 直接暴露测试用）——依赖经真实 FreeSqlEntityDAC 驱动。</summary>
    public static UserRoleViewQueryService CreateRoleViewQueryService(IFreeSql fsql, IDomainUser? user = null)
    {
        var currentUser = user ?? new StubDomainUser();
        if (currentUser is StubDomainUser stub)
            stub.ServiceProvider = BuildProvider(fsql, typeof(UserRoleView));
        return new UserRoleViewQueryService(currentUser);
    }

    private static StubDomainUser BuildStub(IFreeSql fsql, Type[] entityTypes, Action<IServiceCollection, StubDomainUser>? register = null)
    {
        var user = new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        RegisterDacs(services, entityTypes);
        register?.Invoke(services, user);
        user.ServiceProvider = services.BuildServiceProvider();
        return user;
    }

    private static IServiceProvider BuildProvider(IFreeSql fsql, params Type[] entityTypes)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        RegisterDacs(services, entityTypes);
        return services.BuildServiceProvider();
    }

    /// <summary>注册真实 FreeSqlEntityDAC——IEntityDAC（读写 DataService）+ IEntityReadOnlyDAC（VEntity 只读 DataService）。</summary>
    private static void RegisterDacs(IServiceCollection services, Type[] entityTypes)
    {
        foreach (var entityType in entityTypes)
        {
            var implType = typeof(FreeSqlEntityDAC<>).MakeGenericType(entityType);
            services.AddSingleton(typeof(IEntityDAC<>).MakeGenericType(entityType), implType);
            services.AddSingleton(typeof(IEntityReadOnlyDAC<>).MakeGenericType(entityType), implType);
        }
    }
}

/// <summary>
/// 测试用户桩——实现 <see cref="IDomainUser"/> 最小契约（User/Tenant/认证可配置）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产路径等价实现——具体类（DataService）经
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c> 直建（IDomainUser 参数显式传 this，
/// IEntityDAC 等其余从 DI 解析——对齐 Settings/Tagging 测试桩）；接口（IDomainService，如 UserManager 的
/// <c>Use&lt;IUserStore&gt;()</c>）经 <c>provider.GetRequiredService(t)</c> 解析（测试注册的门面实例；
/// 守卫工厂在集成测试经真实 DomainUser 验证，桩内不重复守卫）。</para>
/// </summary>
internal class StubDomainUser(string? userId = null, long? tenantId = null, bool isAuthenticated = false) : IDomainUser
{
    /// <summary>ServiceProvider（测试工厂注册时注入——User.Use&lt;T&gt;() 解析源）。</summary>
    public IServiceProvider? ServiceProvider { get; set; }

    public string SessionKey => "test-session";
    public bool IsAuthenticated => isAuthenticated;
    public bool IsSystemActor => false;
    public IUserInfo? UserInfo => null;
    public long? TenantId => tenantId;
    public bool IsNoAuditActive => false;
    public string? UserId => userId;
    public string? UserName => "test";
    public bool IsInRole(string role) => false;

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        if (ServiceProvider is null)
            throw new NotSupportedException("Stub: Use<T> 未注入 ServiceProvider");

        // 接口（IDomainService）：DI 解析（测试注册的门面实例——UserManager.Use<IUserStore>() 等 AOP 懒加载路径）
        if (typeof(TDomainService).IsInterface)
            return ServiceProvider.GetRequiredService<TDomainService>();

        // 具体类：生产 NoAop 路径等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this——IEntityDAC 从 DI 解析）
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

/// <summary>认证用户桩——具 userId 的 IDomainUser（V0.4.0 仅本人测试用）。</summary>
internal sealed class AuthenticatedStubUser(string userId) : StubDomainUser(userId: userId, isAuthenticated: true);
