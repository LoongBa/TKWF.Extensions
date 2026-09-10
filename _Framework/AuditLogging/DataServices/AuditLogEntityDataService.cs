using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuditLogging;
using TKWF.Ext.AuditLogging.DTOs;

namespace TKWF.Ext.AuditLogging;

/// <summary>数据服务：&#x5BA1;&#x8BA1;&#x65E5;&#x5FD7;&#x8868;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x8BB0;&#x5F55;&#x65B9;&#x6CD5;&#x7EA7;&#x8C03;&#x7528;&#x4E8B;&#x4EF6;&#xFF08;&#x8C03;&#x7528;&#x8005;&#x3001;&#x76EE;&#x6807;&#x65B9;&#x6CD5;&#x3001;&#x53C2;&#x6570;&#x8131;&#x654F; JSON&#x3001;&#x8017;&#x65F6;&#x3001;&#x6210;&#x529F;/&#x5F02;&#x5E38;&#x3001;&#x5173;&#x8054; ID&#xFF09;&#x3002;     &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;     SG1 &#x81EA;&#x52A8;&#x751F;&#x6210; &lt;see cref=&quot;!:TKW.Framework.Domain.IDomainEntity&quot;/&gt; &#x90E8;&#x5206;&#x4E0E; DTO/DataService&#x3002;&lt;/para&gt;     &lt;para&gt;&#x4FDD;&#x7559; BCL &lt;c&gt;[Table(&quot;AuditLog&quot;)]&lt;/c&gt;&#xFF08;&lt;c&gt;FreeSqlTableStructureSynchronizer&lt;/c&gt; &#x9760;&#x5B83;&#x53D1;&#x73B0;&#x5B9E;&#x4F53;&#x5EFA;&#x8868;&#xFF09;&#xFF1B;     &#x5217;&#x6620;&#x5C04;&#x7528; FreeSql &lt;c&gt;[Column]&lt;/c&gt;&#xFF08;IsPrimary/IsIdentity/Position&#xFF0C;&#x5168;&#x9650;&#x5B9A;&#x907F;&#x514D;&#x4E0E; BCL Schema &#x7279;&#x6027;&#x540D;&#x51B2;&#x7A81;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuditLogEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
//
// 【V0.3.0 统计聚合 + 保留天数清理（对齐 SecurityLog v0.2.0 范式）】
// - TopN 聚合（CountByServiceAsync/CountByUserAsync）：内存 GroupBy（SQL WHERE 下推 ExecutionTime 范围 +
//   Take(100_000) 安全上限 + LINQ GroupBy + OrderByDescending + Take(topN)）——IQueryable 桥接不支持 GroupBy 翻译。
// - GetStatsAsync：SQL 级聚合（Dac.CountAsync SQL COUNT × 分区 + FreeSqlQueryableExtensions.AvgAsync/MaxAsync
//   SQL AVG/MAX）；每聚合独立 QueryForUser() 起新查询（FreeSql ISelect 原地可变陷阱）。
// - DeleteExpiredAsync：物理批量删（hasSoftDelete:false——绝不用 EntitySoftDeleteAsync）。
partial class AuditLogEntityDataService(IDomainUser user, IEntityDAC<AuditLogEntity> dac)
        : DomainDataServiceBase<AuditLogEntity, AuditLogEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }

    // ── 数据访问红线整改（2026-09-07）：AuditLogStore/AuditLogQueryService 委托路径的业务方法 ──

    /// <summary>聚合拉取安全上限——内存 GroupBy 防全表拉取（对齐 SecurityLog GetTopFailedByUserAsync
    /// <c>Take(100_000)</c> 先例）。</summary>
    private const int AggregationFetchLimit = 100_000;

    /// <summary>按 predicate 计数（SQL COUNT——V0.3.0 修复：原实现 EntitySelectAsync(0, int.MaxValue) 内存计数全文拉取，
    /// 改为委托基类 SQL COUNT（QueryForUser + Dac.CountAsync 下推）；签名保留避免 QueryService 破坏）。</summary>
    public override Task<long> CountAsync(Expression<Func<AuditLogEntity, bool>>? predicate, CancellationToken ct = default)
        => base.CountAsync(predicate, ct);

    /// <summary>
    /// 调用次数 TopN 聚合（按服务名）——<see cref="IAuditLogAnalyticsService.GetTopServicesAsync"/> 委托路径。
    /// <para>实现说明（对齐 SecurityLog <c>GetTopFailedByUserAsync</c> 范式）：IQueryable 桥接不支持 GroupBy 翻译 →
    /// TopN 分组用<b>内存 GroupBy</b>——SQL WHERE 下推 ExecutionTime 范围 + <c>Dac.ToListAsync(query.Take(100_000))</c>
    /// 安全上限 + LINQ GroupBy + OrderByDescending(Count) + Take(topN)。空白 ServiceName 记录跳过（不构成审计维度）。</para>
    /// </summary>
    /// <param name="fromUtc">起始时间（ExecutionTime &gt;=，闭区间下界；null = 不限）。</param>
    /// <param name="toUtc">结束时间（ExecutionTime &lt;=，闭区间上界；null = 不限）。</param>
    /// <param name="topN">返回条数（按 Count 降序取前 N；调用方保证 &gt; 0）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<List<AuditLogDimensionCount>> CountByServiceAsync(
        DateTime? fromUtc, DateTime? toUtc, int topN, CancellationToken ct = default)
    {
        var query = BuildWindowQuery(fromUtc, toUtc);
        var rows = await Dac.ToListAsync(query.Take(AggregationFetchLimit), ct);
        return rows
            .Where(e => !string.IsNullOrWhiteSpace(e.ServiceName))
            .GroupBy(e => e.ServiceName!)
            .Select(g => new AuditLogDimensionCount(g.Key, g.LongCount()))
            .OrderByDescending(s => s.Count)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// 调用次数 TopN 聚合（按用户名）——<see cref="IAuditLogAnalyticsService.GetTopUsersAsync"/> 委托路径。
    /// <para>实现同 <see cref="CountByServiceAsync"/>（内存 GroupBy 范式）；<b>UserName 为 null/空白的记录跳过</b>
    /// （匿名调用无用户名，不构成可审计维度，避免空桶污染 TopN）。</para>
    /// </summary>
    public async Task<List<AuditLogDimensionCount>> CountByUserAsync(
        DateTime? fromUtc, DateTime? toUtc, int topN, CancellationToken ct = default)
    {
        var query = BuildWindowQuery(fromUtc, toUtc);
        var rows = await Dac.ToListAsync(query.Take(AggregationFetchLimit), ct);
        return rows
            .Where(e => !string.IsNullOrWhiteSpace(e.UserName))
            .GroupBy(e => e.UserName!)
            .Select(g => new AuditLogDimensionCount(g.Key, g.LongCount()))
            .OrderByDescending(s => s.Count)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// SQL 级聚合统计——<see cref="IAuditLogAnalyticsService.GetStatsAsync"/> 委托路径。
    /// <para>全部聚合在数据库层完成（对齐 BackgroundJobs <c>GetStatsAsync</c> 范式，Oracle C1 禁内存 GroupBy）：
    /// 计数 <c>Dac.CountAsync</c>（SQL COUNT(*)，条件 WHERE 下推）——Total / Succeeded / Failed 分区；
    /// 耗时 <see cref="FreeSqlQueryableExtensions.AvgAsync"/> / <see cref="FreeSqlQueryableExtensions.MaxAsync"/>
    /// （SQL AVG/MAX，ADR15 聚合 API 分层——IQueryable 桥接不支持 GroupBy 翻译，走 FreeSql ISelect 原生聚合下推）。</para>
    /// <para>实现注意：FreeSql <c>ISelect</c> 链式 <c>Where</c> 是<b>原地可变</b>的（返回 this），
    /// 故每个聚合独立 <c>QueryForUser()</c> 起新查询，避免前序过滤串扰后续计数。</para>
    /// </summary>
    /// <param name="fromUtc">起始时间（ExecutionTime &gt;=，闭区间下界；null = 不限）。</param>
    /// <param name="toUtc">结束时间（ExecutionTime &lt;=，闭区间上界；null = 不限）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<AuditLogStats> GetStatsAsync(
        DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var baseFilter = BuildWindowFilter(fromUtc, toUtc);

        // SQL COUNT（分区互斥且完备：成功/失败二分）——每计数独立查询防 ISelect 串扰
        var total = await Dac.CountAsync(QueryForUser().Where(baseFilter), ct);
        var succeeded = await Dac.CountAsync(QueryForUser().Where(baseFilter).Where(e => e.Success), ct);
        var failed = await Dac.CountAsync(QueryForUser().Where(baseFilter).Where(e => !e.Success), ct);

        // SQL AVG/MAX（空表时 FreeSql 聚合返回默认值——跳过避免无意义查询）
        double avgDurationMs = 0;
        long maxDurationMs = 0;
        if (total > 0)
        {
            avgDurationMs = await QueryForUser().Where(baseFilter).AvgAsync(e => (decimal)e.DurationMs, ct);
            maxDurationMs = await QueryForUser().Where(baseFilter).MaxAsync(e => e.DurationMs, ct);
        }

        return new AuditLogStats(total, succeeded, failed, avgDurationMs, maxDurationMs);
    }

    /// <summary>
    /// 保留天数清理——物理删除 <c>ExecutionTime &lt; cutoffUtc</c> 的过期记录（分批）。
    /// <para>实现：先查过期 Id 列表（<c>Take(batchSize)</c>——分批防长事务/大锁），再 <c>EntityDeleteBatchAsync(ids)</c>
    /// 物理批量删（<c>hasSoftDelete:false</c>——<c>EntitySoftDeleteAsync</c> 对未启用软删实体抛
    /// <c>InvalidOperationException</c>，故绝不能用）。返回本批删除条数（调用方循环直至 &lt; batchSize）。</para>
    /// </summary>
    /// <param name="cutoffUtc">截止时间（UTC）——仅删 <c>ExecutionTime &lt; cutoffUtc</c> 的记录。</param>
    /// <param name="batchSize">单批最大删除条数（&gt; 0）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default)
    {
        var rows = await Dac.ToListAsync(
            QueryForUser().Where(e => e.ExecutionTime < cutoffUtc).Take(batchSize), ct);
        if (rows.Count == 0) return 0;
        return await EntityDeleteBatchAsync(rows.Select(r => r.Id), ct);
    }

    /// <summary>构建聚合/统计的窗口过滤表达式——ExecutionTime 闭区间（null = 不限）。
    /// <para>每个聚合独立经 <see cref="Query"/> 起新查询（FreeSql ISelect 原地可变陷阱——不跨调用复用）。</para></summary>
    private static Expression<Func<AuditLogEntity, bool>> BuildWindowFilter(DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc.HasValue && toUtc.HasValue)
            return e => e.ExecutionTime >= fromUtc.Value && e.ExecutionTime <= toUtc.Value;
        if (fromUtc.HasValue)
            return e => e.ExecutionTime >= fromUtc.Value;
        if (toUtc.HasValue)
            return e => e.ExecutionTime <= toUtc.Value;
        return e => true;
    }

    /// <summary>构建带窗口过滤的可组合 IQueryable（ExecutionTime 闭区间）。</summary>
    private IQueryable<AuditLogEntity> BuildWindowQuery(DateTime? fromUtc, DateTime? toUtc)
        => QueryForUser().Where(BuildWindowFilter(fromUtc, toUtc));
}