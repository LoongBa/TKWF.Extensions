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

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。
/// <para>ADR88/DI004：生产经 IDomainUser.Use&lt;T&gt;() 懒加载能力——StubDomainUser 增
/// 能力注册表（Register&lt;TService&gt; 按类型键）+ ServiceProvider 兜底双通道解析。</para></summary>
internal sealed class StubDomainUser : IDomainUser
{
    private readonly Dictionary<Type, object> _manual = new();

    /// <summary>可选 IServiceProvider 兜底——未注册能力时经 DI GetRequiredService 解析。</summary>
    public IServiceProvider? ServiceProvider { get; set; }

    public string SessionKey => "test-session";
    public bool IsAuthenticated => false;
    public bool IsSystemActor => false;
    public IUserInfo? UserInfo => null;
    public long? TenantId => null;
    public bool IsNoAuditActive => false;
    public string? UserId => null;
    public string? UserName => null;
    public bool IsInRole(string role) => false;

    /// <summary>注册能力实例（按 typeof(TService) 键）——测试为 Use&lt;T&gt;() 提供显式解析。</summary>
    public void Register<TService>(TService instance)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));
        lock (_manual) _manual[typeof(TService)] = instance;
    }

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        lock (_manual)
        {
            if (_manual.TryGetValue(typeof(TDomainService), out var instance))
                return (TDomainService)instance;
        }
        if (ServiceProvider is not null)
            return ServiceProvider.GetRequiredService<TDomainService>();
        throw new NotSupportedException($"Stub: Use<{typeof(TDomainService).Name}> 未注册——测试须经 Register<TService> 或 ServiceProvider 提供能力");
    }

    public TService GetService<TService>() where TService : notnull
        => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}