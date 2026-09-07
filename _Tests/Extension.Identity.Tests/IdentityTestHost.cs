using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// 测试公共设施——StubDomainUser + 基于 FreeSqlEntityDAC 的 Store 工厂。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与 Settings/BlobStoring 测试同模式）。</para>
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

    /// <summary>创建基于 SQLite 内存库的 UserStore（3 DataService 委托 + VEntity 只读 DataService）。</summary>
    public static UserStore CreateUserStore(IFreeSql fsql)
    {
        var userDac = new FreeSqlEntityDAC<UserEntity>(new UnitOfWorkManager(fsql));
        var userRoleDac = new FreeSqlEntityDAC<UserRoleEntity>(new UnitOfWorkManager(fsql));
        var userRoleViewDac = new FreeSqlEntityDAC<UserRoleView>(new UnitOfWorkManager(fsql));
        var userDataService = new UserEntityDataService(new StubDomainUser(), userDac);
        var userRoleDataService = new UserRoleEntityDataService(new StubDomainUser(), userRoleDac);
        var userRoleViewDataService = new UserRoleViewDataService(new StubDomainUser(), userRoleViewDac);
        return new UserStore(userDataService, userRoleDataService, userRoleViewDataService, NullLogger<UserStore>.Instance);
    }

    /// <summary>创建基于 SQLite 内存库的 RoleStore（2 DataService 委托）。</summary>
    public static RoleStore CreateRoleStore(IFreeSql fsql)
    {
        var roleDac = new FreeSqlEntityDAC<RoleEntity>(new UnitOfWorkManager(fsql));
        var userRoleDac = new FreeSqlEntityDAC<UserRoleEntity>(new UnitOfWorkManager(fsql));
        var roleDataService = new RoleEntityDataService(new StubDomainUser(), roleDac);
        var userRoleDataService = new UserRoleEntityDataService(new StubDomainUser(), userRoleDac);
        return new RoleStore(roleDataService, userRoleDataService, NullLogger<RoleStore>.Instance);
    }
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。</summary>
internal sealed class StubDomainUser : IDomainUser
{
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
        => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");

    public TService GetService<TService>() where TService : notnull
        => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
