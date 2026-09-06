using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions.Tests;

/// <summary>
/// 测试公共设施——StubDomainUser + 基于 mock DAC 的 DataService 工厂。
/// <para>数据访问红线整改（2026-09-07）：EntityDACPermissionStore 委托 DataService——
/// 测试构造 PermissionGrantEntityDataService(StubDomainUser, IEntityDAC&lt;PermissionGrantEntity&gt;)。</para>
/// </summary>
internal static class PermissionsTestHost
{
    public static PermissionGrantEntityDataService CreateDataService(IEntityDAC<PermissionGrantEntity> dac)
        => new(new StubDomainUser(), dac);
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