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
/// 测试公共设施——StubDomainUser + 基于 FreeSqlEntityDAC 的 Store 工厂。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与 Settings/BlobStoring 测试同模式）。</para>
/// <para>ADR88/DI004（A 批整改）：Store 构造不再注入 DataService——经 <c>User.Use&lt;T&gt;()</c> 懒加载解析。
/// 测试工厂构建 StubDomainUser + 能力容器（注册真实 DAC 驱动的 DataService 实例），注入 ServiceProvider。</para>
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

    /// <summary>构建 StubDomainUser + 能力容器（direct-new 工厂模式）：注册真实 DAC 驱动的 DataService 实例。</summary>
    private static StubDomainUser CreateStub(IFreeSql fsql, Action<IServiceCollection, StubDomainUser> register)
    {
        var stub = new StubDomainUser();
        var services = new ServiceCollection();
        register(services, stub);
        stub.ServiceProvider = services.BuildServiceProvider();
        return stub;
    }

    /// <summary>创建基于 SQLite 内存库的 UserStore（3 DataService 委托 + VEntity 只读 DataService）。</summary>
    public static UserStore CreateUserStore(IFreeSql fsql)
    {
        var stub = CreateStub(fsql, (s, user) =>
        {
            var uow = new UnitOfWorkManager(fsql);
            s.AddSingleton(new UserEntityDataService(user, new FreeSqlEntityDAC<UserEntity>(uow)));
            s.AddSingleton(new UserRoleEntityDataService(user, new FreeSqlEntityDAC<UserRoleEntity>(uow)));
            s.AddSingleton(new UserRoleViewDataService(user, new FreeSqlEntityDAC<UserRoleView>(uow)));
        });
        return new UserStore(stub, NullLogger<UserStore>.Instance);
    }

    /// <summary>构建仅解析 IUserManager 的 StubDomainUser（IdentityPasswordManager/IdentityRoleProvider 测试用——
    /// 经基类 User.Use&lt;IUserManager&gt; 懒加载解析，测试注入能力容器）。</summary>
    public static StubDomainUser CreateUserManagerStub(IUserManager manager)
    {
        var stub = new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton<IUserManager>(manager);
        stub.ServiceProvider = services.BuildServiceProvider();
        return stub;
    }

    /// <summary>创建基于 SQLite 内存库的 RoleStore（2 DataService 委托）。</summary>
    public static RoleStore CreateRoleStore(IFreeSql fsql)
    {
        var stub = CreateStub(fsql, (s, user) =>
        {
            var uow = new UnitOfWorkManager(fsql);
            s.AddSingleton(new RoleEntityDataService(user, new FreeSqlEntityDAC<RoleEntity>(uow)));
            s.AddSingleton(new UserRoleEntityDataService(user, new FreeSqlEntityDAC<UserRoleEntity>(uow)));
        });
        return new RoleStore(stub, NullLogger<RoleStore>.Instance);
    }

    /// <summary>创建 UserManager（UserStore + RoleStore 组合，V0.3.0 测试用）。</summary>
    public static IUserManager CreateUserManager(IFreeSql fsql, IdentityOptions? options = null)
    {
        var stub = CreateStub(fsql, (s, user) =>
        {
            var uow = new UnitOfWorkManager(fsql);
            s.AddSingleton(new UserEntityDataService(user, new FreeSqlEntityDAC<UserEntity>(uow)));
            s.AddSingleton(new UserRoleEntityDataService(user, new FreeSqlEntityDAC<UserRoleEntity>(uow)));
            s.AddSingleton(new UserRoleViewDataService(user, new FreeSqlEntityDAC<UserRoleView>(uow)));
            s.AddSingleton(new RoleEntityDataService(user, new FreeSqlEntityDAC<RoleEntity>(uow)));
            s.AddSingleton<IUserStore>(new UserStore(user, NullLogger<UserStore>.Instance));
            s.AddSingleton<IRoleStore>(new RoleStore(user, NullLogger<RoleStore>.Instance));
        });
        return new UserManager(stub, Options.Create(options ?? new IdentityOptions()), NullLogger<UserManager>.Instance);
    }

    /// <summary>创建 IdentityAuthService（V0.3.0 注册/登录测试用）。
    /// <para>ADR88/DI004：构造不再注入 IUserManager——经基类 User.Use 懒加载解析，测试注入能力容器。</para></summary>
    public static IdentityAuthService CreateAuthService(IFreeSql fsql)
    {
        var stub = CreateStub(fsql, (s, user) =>
        {
            var uow = new UnitOfWorkManager(fsql);
            s.AddSingleton(new UserEntityDataService(user, new FreeSqlEntityDAC<UserEntity>(uow)));
            s.AddSingleton(new UserRoleEntityDataService(user, new FreeSqlEntityDAC<UserRoleEntity>(uow)));
            s.AddSingleton(new UserRoleViewDataService(user, new FreeSqlEntityDAC<UserRoleView>(uow)));
            s.AddSingleton(new RoleEntityDataService(user, new FreeSqlEntityDAC<RoleEntity>(uow)));
            s.AddSingleton<IUserStore>(new UserStore(user, NullLogger<UserStore>.Instance));
            s.AddSingleton<IRoleStore>(new RoleStore(user, NullLogger<RoleStore>.Instance));
            s.AddSingleton<IUserManager>(new UserManager(user, Options.Create(new IdentityOptions()), NullLogger<UserManager>.Instance));
        });
        return new IdentityAuthService(stub);
    }

    /// <summary>创建 UserRoleViewQueryService（V0.4.0 REST 直接暴露测试用）——对齐
    /// <c>UserRoleViewDataService</c> 手动构造先例（L43），依赖经真实 FreeSqlEntityDAC 驱动。
    /// ADR88：构造不再注入 DataService——经基类 User.Use 懒加载解析，测试注入能力容器。</summary>
    public static UserRoleViewQueryService CreateRoleViewQueryService(IFreeSql fsql, IDomainUser? user = null)
    {
        var currentUser = user ?? new StubDomainUser();
        if (currentUser is StubDomainUser stub)
        {
            var services = new ServiceCollection();
            var uow = new UnitOfWorkManager(fsql);
            services.AddSingleton(new UserRoleViewDataService(currentUser, new FreeSqlEntityDAC<UserRoleView>(uow)));
            stub.ServiceProvider = services.BuildServiceProvider();
        }
        return new UserRoleViewQueryService(currentUser);
    }
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。
/// <para>ADR88/DI004（A 批整改）：Use&lt;T&gt;()/GetService&lt;T&gt;() 从注入的 ServiceProvider 容器解析
/// （测试工厂经 <see cref="ServiceProvider"/> 注入；未注入时保留原抛 NotSupportedException 语义）。</para></summary>
internal class StubDomainUser : IDomainUser
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
    public virtual string? UserId => null;
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

/// <summary>认证用户桩——具 userId 的 IDomainUser（V0.4.0 仅本人测试用）。</summary>
internal sealed class AuthenticatedStubUser(string userId) : StubDomainUser
{
    public override string? UserId => userId;
}