using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuditLogging;
using TKWF.Ext.AuditLogging.DTOs;

namespace TKWF.Ext.AuditLogging;

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
//
// 【V0.4.0 管理 API（Oracle P1-1~6 + P2-1~5 修订）】
// - [GenerateController(FromDataService=true, ExcludeMethods=*)]——排除含 ArgumentsJson 的标准 CRUD
//   （GetById/Select/SelectPage/Create/Update/Count）防 D5 泄露 + 防伪造审计；仅保留 DeleteAsync 单条删除。
// - 5 管理方法：SearchLogsAsync（列表裁剪 DTO）/ GetDetailAsync（含 ArgumentsJson，权限门控）/
//   CleanupAsync（直接实现批量循环——不委托 AnalyticsService 避免循环依赖，P1-4）/
//   GetStatsAsync（统计）/ DeleteAsync（单条删除）。
// - DataService 改 public sealed partial（[GenerateController] 须 public——消费方 SG1 跨程序集引用，P1-6）。
/// <summary>V0.4.0 管理 API 端点——经 ExcludeMethods 排除含 ArgumentsJson 的标准 CRUD，仅暴露受控自定义端点。</summary>
[GenerateController(FromDataService = true,
    ExcludeMethods = new[] { "GetByIdAsync", "SelectAsync", "SelectPageAsync", "CountAsync", "CreateAsync", "UpdateAsync" })]
public sealed partial class AuditLogEntityDataService(IDomainUser user, IEntityDAC<AuditLogEntity> dac)
        : DomainDataServiceBase<AuditLogEntity, AuditLogEntityDto>(user, dac, hasSoftDelete: false) 
{
     /// <summary>场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）。</summary>
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
    [GenerateControllerMethod]   // V0.4.0 管理 API（Oracle P1-5）：统计端点暴露
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

    // ── V0.4.0 管理 API（Oracle P1-1~6 修订） ──

    /// <summary>
    /// 审计日志列表查询（管理 API）——返回**裁剪 DTO**（不含 ArgumentsJson，D5 保持，Oracle P1-2）。
    /// <para>与 <see cref="IAuditLogQueryService.GetListAsync"/> 共享同一查询路径（QueryService 委托本方法，单一真相源 P2-2）：
    /// 谓词构建（BuildQueryPredicate）+ 分页 + 裁剪映射在此集中。</para>
    /// </summary>
    [GenerateControllerMethod]
    public async Task<AuditLogPagedResult> SearchLogsAsync(AuditLogQueryInput query, CancellationToken ct = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        var (skip, take) = NormalizePaging(query.Skip, query.Take);
        var predicate = BuildQueryPredicate(query);

        var countTask = CountAsync(predicate, ct);
        var listTask = EntitySelectAsync(
            predicate, skip, take, q => q.OrderByDescending(e => e.ExecutionTime), ct);

        await Task.WhenAll(countTask, listTask);

        var total = await countTask;
        var entities = await listTask;

        return new AuditLogPagedResult(total, entities.Select(MapToListItemDto).ToList());
    }

    /// <summary>审计日志详情（管理 API）——含 ArgumentsJson（Oracle P2-5 厘清：详情 DTO 显式暴露，须消费方控制器级 <c>[RequirePermission]</c> 门控）。</summary>
    [GenerateControllerMethod]
    public async Task<AuditLogDetailDto?> GetDetailAsync(long id, CancellationToken ct = default)
    {
        var entity = await EntityGetAsync(e => e.Id == id, ct);
        if (entity == null) return null;
        return new AuditLogDetailDto(
            entity.Id, entity.UserName, entity.UserId, entity.ServiceName, entity.MethodName,
            entity.ArgumentsJson, entity.ExecutionTime, entity.DurationMs, entity.Success,
            entity.Exception, entity.CorrelationId, entity.CreateTime);
    }

    /// <summary>
    /// 保留天数清理（管理 API）——**直接实现批量循环**（Oracle P1-4：不委托 AnalyticsService——避免循环依赖）。
    /// <para>对齐 BackgroundJobs 清理范式：<c>MaxRounds</c> 死循环保护 + 循环内 <c>ct.ThrowIfCancellationRequested()</c>；
    /// 分批 <c>DeleteExpiredAsync(cutoffUtc, CleanupBatchSize)</c> 直至清完或达轮次上限。
    /// 配置经 <c>IDomainUser.GetService&lt;IOptions&lt;AuditLoggingOptions&gt;&gt;()</c> 解析（DataService 主构造函数固定，无额外注入——P1-4 定稿）。</para>
    /// </summary>
    [GenerateControllerMethod]
    public async Task<int> CleanupAsync(CancellationToken ct = default)
    {
        var options = user.GetOptionalService<IOptions<AuditLoggingOptions>>()?.Value
            ?? new AuditLoggingOptions();   // 测试宿主/无配置回退默认（RetentionDays 90/CleanupBatchSize 500）
        var cutoffUtc = DateTime.UtcNow.AddDays(-options.RetentionDays);
        var batchSize = Math.Max(1, options.CleanupBatchSize);

        const int maxRounds = 100;   // 死循环保护（对齐 BackgroundJobs MaxRounds）
        int totalDeleted = 0;

        for (int round = 0; round < maxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            int deleted = await DeleteExpiredAsync(cutoffUtc, batchSize, ct);
            if (deleted == 0) break;
            totalDeleted += deleted;
            if (deleted < batchSize) break;
        }
        return totalDeleted;
    }

    /// <summary>统计（管理 API）——暴露现有 <see cref="GetStatsAsync"/>（SQL 级聚合，Oracle P1-5）。</summary>
    /// <summary>单条物理删除（管理 API）——经基类 DeleteAsync 分派（hasSoftDelete:false → 物理删），推翻 v0.3.0 "无单条删除"限定。
    /// <c>new</c> 有意隐藏基类同签名方法（此方法加 [GenerateControllerMethod] 标注暴露端点，CS0108 预期）。</summary>
    [GenerateControllerMethod]
    public new Task<bool> DeleteAsync(long id, CancellationToken ct = default)
        => base.DeleteAsync(id, ct);

    /// <summary>构建查询过滤 predicate（10 条件 AND 组合）——集中于此供 SearchLogsAsync + QueryService 共享（P2-2 单一真相源）。</summary>
    internal static Expression<Func<AuditLogEntity, bool>>? BuildQueryPredicate(AuditLogQueryInput query)
    {
        var param = Expression.Parameter(typeof(AuditLogEntity), "e");
        Expression? combined = null;

        if (query.StartTime.HasValue)
            combined = CombinePredicate(combined, Expression.GreaterThanOrEqual(
                Expression.Property(param, nameof(AuditLogEntity.ExecutionTime)),
                Expression.Constant(query.StartTime.Value)));

        if (query.EndTime.HasValue)
            combined = CombinePredicate(combined, Expression.LessThanOrEqual(
                Expression.Property(param, nameof(AuditLogEntity.ExecutionTime)),
                Expression.Constant(query.EndTime.Value)));

        if (!string.IsNullOrEmpty(query.UserName))
        {
            var userNameProp = Expression.Property(param, nameof(AuditLogEntity.UserName));
            var contains = Expression.Call(
                Expression.Coalesce(userNameProp, Expression.Constant(string.Empty)),
                nameof(string.Contains),
                Type.EmptyTypes,
                Expression.Constant(query.UserName));
            combined = CombinePredicate(combined, contains);
        }

        if (!string.IsNullOrEmpty(query.UserId))
            combined = CombinePredicate(combined, Expression.Equal(
                Expression.Property(param, nameof(AuditLogEntity.UserId)),
                Expression.Constant(query.UserId)));

        if (!string.IsNullOrEmpty(query.ServiceName))
            combined = CombinePredicate(combined, Expression.Equal(
                Expression.Property(param, nameof(AuditLogEntity.ServiceName)),
                Expression.Constant(query.ServiceName)));

        if (!string.IsNullOrEmpty(query.MethodName))
            combined = CombinePredicate(combined, Expression.Equal(
                Expression.Property(param, nameof(AuditLogEntity.MethodName)),
                Expression.Constant(query.MethodName)));

        if (query.Success.HasValue)
            combined = CombinePredicate(combined, Expression.Equal(
                Expression.Property(param, nameof(AuditLogEntity.Success)),
                Expression.Constant(query.Success.Value)));

        if (!string.IsNullOrEmpty(query.CorrelationId))
            combined = CombinePredicate(combined, Expression.Equal(
                Expression.Property(param, nameof(AuditLogEntity.CorrelationId)),
                Expression.Constant(query.CorrelationId)));

        if (query.MinDurationMs.HasValue)
            combined = CombinePredicate(combined, Expression.GreaterThanOrEqual(
                Expression.Property(param, nameof(AuditLogEntity.DurationMs)),
                Expression.Constant(query.MinDurationMs.Value)));

        if (query.MaxDurationMs.HasValue)
            combined = CombinePredicate(combined, Expression.LessThanOrEqual(
                Expression.Property(param, nameof(AuditLogEntity.DurationMs)),
                Expression.Constant(query.MaxDurationMs.Value)));

        return combined == null ? null : Expression.Lambda<Func<AuditLogEntity, bool>>(combined, param);
    }

    private static Expression CombinePredicate(Expression? left, Expression right)
        => left == null ? right : Expression.AndAlso(left, right);

    /// <summary>规范化分页——Take 默认 50 上限 200；Skip 下限 0（对齐 QueryService）。</summary>
    internal static (int Skip, int Take) NormalizePaging(int skip, int take)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
        return (skip, take);
    }

    /// <summary>实体 → 列表 DTO（不含 ArgumentsJson——D5）。</summary>
    internal static AuditLogListItemDto MapToListItemDto(AuditLogEntity entity)
        => new()
        {
            Id = entity.Id,
            UserName = entity.UserName,
            UserId = entity.UserId,
            ServiceName = entity.ServiceName,
            MethodName = entity.MethodName,
            ExecutionTime = entity.ExecutionTime,
            DurationMs = entity.DurationMs,
            Success = entity.Success,
            Exception = entity.Exception,
            CorrelationId = entity.CorrelationId,
            CreateTime = entity.CreateTime
        };

    private const int DefaultTake = 50;
    private const int MaxTake = 200;
}