using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataPort.Tests;

/// <summary>
/// 测试公共设施——StubDomainUser + 基于 FreeSqlEntityDAC 的 DataService 工厂。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store/Service 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与其它扩展测试同模式）。</para>
/// </summary>
internal static class DataPortTestHost
{
    /// <summary>创建基于 SQLite 内存库的 DataImportRecordEntityDataService（真实 FreeSql DAC 驱动）。</summary>
    public static DataImportRecordEntityDataService CreateDataService(IFreeSql fsql)
    {
        var dac = new FreeSqlEntityDAC<DataImportRecordEntity>(new UnitOfWorkManager(fsql));
        return new DataImportRecordEntityDataService(new StubDomainUser(), dac);
    }
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。
/// <para>ADR88 适配：Use&lt;T&gt;() 懒加载从服务映射表解析（<see cref="With{T}"/> 注册外部 DataService 实例）。</para></summary>
internal sealed class StubDomainUser : IDomainUser
{
    private readonly Dictionary<Type, object? > _services = new();

    /// <summary>注册外部服务实例（懒加载 Use&lt;T&gt;() 解析源——测试构造 DataService 后注册）。</summary>
    public StubDomainUser With<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
        return this;
    }

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
        => _services.TryGetValue(typeof(TDomainService), out var svc) && svc is TDomainService s
            ? s
            : throw new NotSupportedException($"Stub: {typeof(TDomainService).Name} 未注册——请用 With<T>() 注册（懒加载 Use<T> 解析源）");

    public TService GetService<TService>() where TService : notnull
        => _services.TryGetValue(typeof(TService), out var svc) && svc is TService s
            ? s
            : throw new NotSupportedException($"Stub: {typeof(TService).Name} 未注册——请用 With<T>() 注册（懒加载 GetService<T> 解析源）");

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
