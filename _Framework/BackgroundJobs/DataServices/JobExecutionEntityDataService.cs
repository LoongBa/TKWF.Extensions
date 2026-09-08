using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.BackgroundJobs;
using TKWF.Ext.BackgroundJobs.DTOs;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 数据服务：作业执行历史实体（V0.1.0，Oracle P1-3 业务方法 partial）——分页过滤 + SQL 级聚合统计 + GetDetailAsync，供 <see cref="T:TKWF.Ext.BackgroundJobs.IJobExecutionQueryService"/> 委托。
/// </summary>
// 提示：标准 CRUD 逻辑和构造函数已由 JobExecutionEntityDataService.g.cs 承载。
partial class JobExecutionEntityDataService(IDomainUser user, IEntityDAC<JobExecutionEntity> dac)
        : DomainDataServiceBase<JobExecutionEntity, JobExecutionEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>按 Id 查实体（GetDetailAsync 定位用）——命名 GetEntityByIdAsync 避开基类 GetByIdAsync(long)（返回 DTO）。</summary>
    public Task<JobExecutionEntity?> GetEntityByIdAsync(long id, CancellationToken ct = default)
        => EntityGetAsync(r => r.Id == id, ct);

    /// <summary>分页过滤查询（列表 DTO 不含 ErrorText——安全决策，对齐 AuditLogging 先例）。</summary>
    public Task<List<JobExecutionEntity>> GetListAsync(
        string? jobId, string? jobType, string? provider,
        bool? isSuccess, bool? isCancelled, long? tenantId,
        DateTime? startFromUtc, DateTime? startToUtc,
        int skip, int take, CancellationToken ct = default)
        => EntitySelectAsync(BuildFilterPredicate(jobId, jobType, provider, isSuccess, isCancelled, tenantId, startFromUtc, startToUtc),
            skip, take, q => q.OrderByDescending(e => e.StartedAtUtc), ct);

    /// <summary>分页总数（JobExecutionPagedResult.Total——与 GetListAsync 同一过滤谓词）。</summary>
    public Task<long> CountAsync(
        string? jobId, string? jobType, string? provider,
        bool? isSuccess, bool? isCancelled, long? tenantId,
        DateTime? startFromUtc, DateTime? startToUtc,
        CancellationToken ct = default)
        => Dac.CountAsync(QueryForUser().Where(BuildFilterPredicate(jobId, jobType, provider, isSuccess, isCancelled, tenantId, startFromUtc, startToUtc)), ct);

    /// <summary>
    /// SQL 级聚合统计（Oracle C1）——全部聚合在数据库层完成，<b>禁 Dac.ToListAsync + 内存 GroupBy</b>：
    /// <list type="bullet">
    /// <item>计数：<c>Dac.CountAsync</c>（SQL COUNT(*)，条件 WHERE 下推）——Total/Succeeded/Failed/Cancelled 四状态分区</item>
    /// <item>耗时：<see cref="TKW.Framework.Domain.FreeSql.FreeSqlQueryableExtensions.AvgAsync"/> /
    /// <see cref="TKW.Framework.Domain.FreeSql.FreeSqlQueryableExtensions.MaxAsync"/>（SQL AVG/MAX，
    /// ADR15 聚合 API 分层——IQueryable 桥接不支持 GroupBy 翻译，FreeSql ISelect 原生聚合下推）</item>
    /// </list>
    /// <para>对齐 Tagging GetFrequencyAsync 范式：WHERE 下推在 DataService partial 内，Service 只委托。</para>
    /// <para>实现注意：FreeSql <c>ISelect</c> 链式 <c>Where</c> 是<b>原地可变</b>的（返回 this），
    /// 故每个聚合独立 <c>QueryForUser()</c> 起新查询，避免前序过滤串扰后续计数（实测 Failed=0 根因）。</para>
    /// </summary>
    public async Task<JobExecutionStats> GetStatsAsync(
        TimeSpan? window, CancellationToken ct = default)
    {
        // 时间窗口过滤（独立表达式，逐聚合复用）
        Expression<Func<JobExecutionEntity, bool>> baseFilter = window.HasValue
            ? e => e.StartedAtUtc >= DateTime.UtcNow - window.Value
            : e => true;

        // SQL COUNT（条件分区互斥且完备：成功/失败/取消三分，取消必非成功）——每计数独立查询防 ISelect 串扰
        var total = await Dac.CountAsync(QueryForUser().Where(baseFilter), ct);
        var succeeded = await Dac.CountAsync(QueryForUser().Where(baseFilter).Where(e => e.IsSuccess && !e.IsCancelled), ct);
        var failed = await Dac.CountAsync(QueryForUser().Where(baseFilter).Where(e => !e.IsSuccess && !e.IsCancelled), ct);
        var cancelled = await Dac.CountAsync(QueryForUser().Where(baseFilter).Where(e => e.IsCancelled), ct);

        // SQL AVG/MAX（空表时 FreeSql 聚合返回默认值——跳过避免无意义查询）
        long avgDurationMs = 0, maxDurationMs = 0;
        if (total > 0)
        {
            avgDurationMs = (long)await QueryForUser().Where(baseFilter).AvgAsync(e => (decimal)e.DurationMs, ct);
            maxDurationMs = await QueryForUser().Where(baseFilter).MaxAsync(e => e.DurationMs, ct);
        }

        return new JobExecutionStats((int)total, (int)succeeded, (int)failed, (int)cancelled, avgDurationMs, maxDurationMs);
    }

    /// <summary>构建过滤谓词（AND 逻辑下推，全部可选条件）。</summary>
    private static Expression<Func<JobExecutionEntity, bool>> BuildFilterPredicate(
        string? jobId, string? jobType, string? provider,
        bool? isSuccess, bool? isCancelled, long? tenantId,
        DateTime? startFromUtc, DateTime? startToUtc)
    {
        Expression<Func<JobExecutionEntity, bool>> predicate = e => true;
        if (!string.IsNullOrEmpty(jobId))
            predicate = CombineAnd(predicate, e => e.JobId == jobId);
        if (!string.IsNullOrEmpty(jobType))
            predicate = CombineAnd(predicate, e => e.JobType == jobType);
        if (!string.IsNullOrEmpty(provider))
            predicate = CombineAnd(predicate, e => e.Provider == provider);
        if (isSuccess.HasValue)
            predicate = CombineAnd(predicate, e => e.IsSuccess == isSuccess.Value);
        if (isCancelled.HasValue)
            predicate = CombineAnd(predicate, e => e.IsCancelled == isCancelled.Value);
        if (tenantId.HasValue)
            predicate = CombineAnd(predicate, e => e.TenantId == tenantId.Value);
        if (startFromUtc.HasValue)
            predicate = CombineAnd(predicate, e => e.StartedAtUtc >= startFromUtc.Value);
        if (startToUtc.HasValue)
            predicate = CombineAnd(predicate, e => e.StartedAtUtc <= startToUtc.Value);
        return predicate;
    }

    /// <summary>合并两个谓词（AND 逻辑）。</summary>
    private static Expression<Func<T, bool>> CombineAnd<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var param = Expression.Parameter(typeof(T));
        var body = Expression.AndAlso(
            Expression.Invoke(left, param),
            Expression.Invoke(right, param));
        return Expression.Lambda<Func<T, bool>>(body, param);
    }
}
