using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// 测试公共设施——StubDomainUser + 基于 FreeSqlEntityDAC 的 Store/QueryService 工厂。
/// <para>数据访问红线合规（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与 AuditLogging/Account 测试同模式）。</para>
/// </summary>
internal static class SecurityLogTestHost
{
    /// <summary>创建基于 SQLite 内存库的 SecurityLogEntityDataService（真实 FreeSql DAC 驱动）。</summary>
    public static SecurityLogEntityDataService CreateDataService(IFreeSql fsql)
    {
        var dac = new FreeSqlEntityDAC<SecurityLogEntity>(new UnitOfWorkManager(fsql));
        return new SecurityLogEntityDataService(new StubDomainUser(), dac);
    }

    /// <summary>创建基于 SQLite 内存库的 SecurityLogStore（DataService 委托）。</summary>
    public static SecurityLogStore CreateStore(IFreeSql fsql)
        => new(CreateDataService(fsql), NullLogger<SecurityLogStore>.Instance);

    /// <summary>创建基于 SQLite 内存库的 SecurityLogQueryService（DataService 委托）。</summary>
    public static SecurityLogQueryService CreateQueryService(IFreeSql fsql)
        => new(CreateDataService(fsql), NullLogger<SecurityLogQueryService>.Instance);

    /// <summary>创建 SQLite 内存库（每次调用新连接 = 独立内存库）+ 同步实体表结构。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<SecurityLogEntity>();
        return fsql;
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
