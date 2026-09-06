using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// 测试公共设施——StubDomainUser + 基于 FreeSqlEntityDAC 的 Store 工厂。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与 Settings/BlobStoring 测试同模式）。</para>
/// </summary>
internal static class AccountTestHost
{
    /// <summary>创建基于 SQLite 内存库的 AccountLockoutStore（DataService 委托）。</summary>
    public static AccountLockoutStore CreateLockoutStore(IFreeSql fsql)
    {
        var dac = new FreeSqlEntityDAC<AccountLockoutEntity>(new UnitOfWorkManager(fsql));
        var dataService = new AccountLockoutEntityDataService(new StubDomainUser(), dac);
        return new AccountLockoutStore(dataService, NullLogger<AccountLockoutStore>.Instance);
    }

    /// <summary>创建基于 SQLite 内存库的 PasswordResetStore（DataService 委托）。</summary>
    public static PasswordResetStore CreatePasswordResetStore(IFreeSql fsql)
    {
        var dac = new FreeSqlEntityDAC<PasswordResetCodeEntity>(new UnitOfWorkManager(fsql));
        var dataService = new PasswordResetCodeEntityDataService(new StubDomainUser(), dac);
        return new PasswordResetStore(dataService, NullLogger<PasswordResetStore>.Instance);
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
