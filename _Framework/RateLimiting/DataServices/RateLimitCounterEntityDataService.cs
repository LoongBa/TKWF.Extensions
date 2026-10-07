using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.RateLimiting;
using TKWF.Ext.RateLimiting.DTOs;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// 限流计数表数据服务——partial 分部（.g.cs 由 xCodeGen 生成承载 CRUD/构造器，不入库；本分部仅编写业务方法）。
/// <para>数据访问红线：Service 层只依赖 DataService（ADR61 自动注册），零 IFreeSql/IEntityDAC 直注入。</para>
/// <para>CAS 原子语义（ADR89 <c>UpdateWhereAsync</c>——单语句条件 UPDATE 天然原子，行锁 + WHERE 重求值；
/// 方案 v0.3.0 §五 R2：防 read-modify-write TOCTOU）：<c>SET Count=Count+1 WHERE Key=@key AND
/// WindowEndUtc&gt;=@now AND Count&lt;@max</c>——affected==1 成功 / ==0 读行分辩（窗口过期重置 / Count 达 max 拒绝）。</para>
/// </summary>
partial class RateLimitCounterEntityDataService(IDomainUser user, IEntityDAC<RateLimitCounterEntity> dac)
    : DomainDataServiceBase<RateLimitCounterEntity, RateLimitCounterEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>
    /// 窗口内 CAS 原子递增（ADR89 单语句条件 UPDATE）：
    /// <c>SET Count=Count+1 WHERE Key=@key AND WindowEndUtc&gt;=@now AND Count&lt;@max</c>。
    /// <para>返回影响行数：1 = 递增成功；0 = 窗口已满（Count&gt;=max）或行不存在（并发首插败者/未插入）。</para>
    /// </summary>
    public Task<int> IncrementIfWithinWindowAsync(string key, DateTime now, int max, CancellationToken ct = default)
        => EntityUpdateWhereAsync(
            where: x => x.Key == key && x.WindowEndUtc >= now && x.Count < max,
            setColumns: x => new RateLimitCounterEntity { Count = x.Count + 1, UpdateTime = DateTime.UtcNow },
            ct: ct);

    /// <summary>
    /// 窗口过期重置（惰性淘汰——过期行随下一次命中该 Key 时重置，无后台清理）：
    /// <c>SET Count=1, WindowStartUtc=@now, WindowEndUtc=@newWindowEnd WHERE Key=@key AND WindowEndUtc&lt;@now</c>。
    /// <para>返回影响行数：1 = 重置成功；0 = 行不存在或窗口未过期（并发竞态守卫）。</para>
    /// </summary>
    public Task<int> ResetWindowAsync(string key, DateTime now, DateTime newWindowEnd, CancellationToken ct = default)
        => EntityUpdateWhereAsync(
            where: x => x.Key == key && x.WindowEndUtc < now,
            setColumns: x => new RateLimitCounterEntity
            {
                Count = 1,
                WindowStartUtc = now,
                WindowEndUtc = newWindowEnd,
                UpdateTime = DateTime.UtcNow,
            },
            ct: ct);

    /// <summary>按计数键查单行（GetRemaining/GetRetryAfter 读路径；首插前判定）。</summary>
    public Task<RateLimitCounterEntity?> GetByKeyAsync(string key, CancellationToken ct = default)
        => EntityGetAsync(x => x.Key == key, ct);

    /// <summary>首插计数底册行（Count=1 + 窗口起止；Key 唯一约束捕获并发首插冲突）。</summary>
    public Task<RateLimitCounterEntity> InsertAsync(RateLimitCounterEntity entity, CancellationToken ct = default)
        => EntityCreateAsync(entity, ct);
}
