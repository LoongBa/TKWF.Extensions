using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Testing.Shared;

/// <summary>
/// 测试用户桩——实现 <see cref="IDomainUser"/> 最小契约；<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价：
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>——IDomainUser 参数显式传 this，其余从 DI 解析
/// （对齐 Settings/DataDictionary 宿主 StubDomainUser 范式，2026-09-14 生成物健康清单 #6 同源）。
/// C 基座公共类（2026-10-04）：统一 16+ 个独立宿主文件 + 10 内联测试类的标准桩定义。
/// </summary>
public sealed class StubDomainUser : IDomainUser
{
    private IServiceProvider? _provider;

    /// <summary>ServiceProvider（测试工厂注册时注入——Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider? ServiceProvider { set => _provider = value; get => _provider; }

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
    {
        // 生产 NoAop 路径等价：ActivatorUtilities 直建，IDomainUser 参数显式传 this，其余从 DI 解析
        if (_provider is null)
            throw new NotSupportedException("Stub: Use<T> 未注入 ServiceProvider");
        return (TDomainService)ActivatorUtilities.CreateInstance(_provider, typeof(TDomainService), this);
    }

    public TService GetService<TService>() where TService : notnull
    {
        if (_provider is null)
            throw new NotSupportedException("Stub: GetService<T> 未注入 ServiceProvider");
        return _provider.GetRequiredService<TService>();
    }

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}