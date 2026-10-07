using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// 限流计数表数据访问门面（ADR90 门面范式）——<see cref="SqlCountRateLimitCheck"/> 的数据访问通道。
/// <para>为何需要门面（tkwf-extension §4.8 #12 接线型 C1）：<see cref="IRateLimitCheck"/> 非 <c>IDomainService</c>，
/// <see cref="SqlCountRateLimitCheck"/> 只能接线型注册（消费方 <c>AddSingleton&lt;IRateLimitCheck, SqlCountRateLimitCheck&gt;</c>，
/// ctor 无 IDomainUser）——而 <see cref="RateLimitCounterEntityDataService"/> 注册为 throw-factory（DI 直取必抛守卫），
/// 运行期只能经 <c>User.Use&lt;具体类&gt;()</c> NoAop 直建（需 IDomainUser）。本门面继承 <see cref="DomainServiceBase"/>
/// + <c>AddConstructibleService</c> 注册（接口可构造守卫工厂）——SqlCount 在<b>域作用域帧内</b>
/// <c>GetRequiredService&lt;IRateLimitCounterDataService&gt;()</c> 解析，守卫工厂从 CurrentAopUser 供给 ctor IDomainUser，
/// 门面再经基类 <c>User.Use&lt;RateLimitCounterEntityDataService&gt;()</c> 懒加载（DI004 零豁免）。帧外解析抛守卫（正确 fail）。</para>
/// <para>纯数据访问委托（无业务语义）——CAS 原子递增 / 窗口重置 / 读行 / 首插全部转发
/// <see cref="RateLimitCounterEntityDataService"/>（红线合规：零 IFreeSql/IEntityDAC 直注入）。</para>
/// </summary>
public interface IRateLimitCounterDataService : IDomainService
{
    /// <summary>窗口内 CAS 原子递增（<c>SET Count=Count+1 WHERE Key=@key AND WindowEndUtc&gt;=@now AND Count&lt;@max</c>）——返回影响行数。</summary>
    Task<int> IncrementIfWithinWindowAsync(string key, DateTime now, int max, CancellationToken ct = default);

    /// <summary>窗口过期重置（<c>SET Count=1, WindowEndUtc=@newWindowEnd WHERE Key=@key AND WindowEndUtc&lt;@now</c>）——返回影响行数。</summary>
    Task<int> ResetWindowAsync(string key, DateTime now, DateTime newWindowEnd, CancellationToken ct = default);

    /// <summary>按计数键读行（GetRemaining/GetRetryAfter 读路径；首插前判定）。</summary>
    Task<RateLimitCounterEntity?> GetByKeyAsync(string key, CancellationToken ct = default);

    /// <summary>首插计数底册行（Key 唯一约束捕获并发首插冲突）。</summary>
    Task<RateLimitCounterEntity> InsertAsync(RateLimitCounterEntity entity, CancellationToken ct = default);
}

/// <summary>限流计数表数据访问门面实现（internal sealed——ADR90 门面范式，消费方经接口解析）。</summary>
[DiContractIgnore]
internal sealed class RateLimitCounterDataService : DomainServiceBase, IRateLimitCounterDataService
{
    private RateLimitCounterEntityDataService? _ds;

    /// <summary>DataService 经基类 User.Use&lt;具体类&gt;() NoAop 懒加载（DI004——禁构造注入领域服务）。</summary>
    private RateLimitCounterEntityDataService Ds => _ds ??= User.Use<RateLimitCounterEntityDataService>();

    public RateLimitCounterDataService(IDomainUser user) : base(user) { }

    /// <inheritdoc />
    public Task<int> IncrementIfWithinWindowAsync(string key, DateTime now, int max, CancellationToken ct = default)
        => Ds.IncrementIfWithinWindowAsync(key, now, max, ct);

    /// <inheritdoc />
    public Task<int> ResetWindowAsync(string key, DateTime now, DateTime newWindowEnd, CancellationToken ct = default)
        => Ds.ResetWindowAsync(key, now, newWindowEnd, ct);

    /// <inheritdoc />
    public Task<RateLimitCounterEntity?> GetByKeyAsync(string key, CancellationToken ct = default)
        => Ds.GetByKeyAsync(key, ct);

    /// <inheritdoc />
    public Task<RateLimitCounterEntity> InsertAsync(RateLimitCounterEntity entity, CancellationToken ct = default)
        => Ds.InsertAsync(entity, ct);
}
