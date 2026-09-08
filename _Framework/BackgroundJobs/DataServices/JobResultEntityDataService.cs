using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.BackgroundJobs;
using TKWF.Ext.BackgroundJobs.DTOs;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 数据服务：业务结果实体（V0.1.0，Oracle P1-3 业务方法 partial）——分页查询 + 按 JobId 查最新，供 <see cref="T:TKWF.Ext.BackgroundJobs.IJobResultQueryService"/> 委托。
/// </summary>
// 提示：标准 CRUD 逻辑和构造函数已由 JobResultEntityDataService.g.cs 承载。
partial class JobResultEntityDataService(IDomainUser user, IEntityDAC<JobResultEntity> dac)
        : DomainDataServiceBase<JobResultEntity, JobResultEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>分页过滤查询（JobId/ResultType/时间范围，按 CreateTime 倒序）。</summary>
    public Task<List<JobResultEntity>> GetListAsync(
        string? jobId, string? resultType,
        DateTime? startFromUtc, DateTime? startToUtc,
        int skip, int take, CancellationToken ct = default)
        => EntitySelectAsync(BuildFilterPredicate(jobId, resultType, startFromUtc, startToUtc),
            skip, take, q => q.OrderByDescending(e => e.CreateTime), ct);

    /// <summary>分页总数（JobResultPagedResult.Total——与 GetListAsync 同一过滤谓词）。</summary>
    public Task<long> CountAsync(
        string? jobId, string? resultType,
        DateTime? startFromUtc, DateTime? startToUtc,
        CancellationToken ct = default)
        => Dac.CountAsync(QueryForUser().Where(BuildFilterPredicate(jobId, resultType, startFromUtc, startToUtc)), ct);

    /// <summary>按 JobId 查最新一条结果。</summary>
    public async Task<JobResultEntity?> GetLatestByJobIdAsync(string jobId, CancellationToken ct = default)
    {
        var results = await EntitySelectAsync(
            e => e.JobId == jobId, 0, 1,
            q => q.OrderByDescending(e => e.CreateTime), ct);
        return results.FirstOrDefault();
    }

    /// <summary>构建过滤谓词（AND 逻辑下推，全部可选条件）。</summary>
    private static Expression<Func<JobResultEntity, bool>> BuildFilterPredicate(
        string? jobId, string? resultType,
        DateTime? startFromUtc, DateTime? startToUtc)
    {
        Expression<Func<JobResultEntity, bool>> predicate = e => true;
        if (!string.IsNullOrEmpty(jobId))
            predicate = CombineAnd(predicate, e => e.JobId == jobId);
        if (!string.IsNullOrEmpty(resultType))
            predicate = CombineAnd(predicate, e => e.ResultType == resultType);
        if (startFromUtc.HasValue)
            predicate = CombineAnd(predicate, e => e.CreateTime >= startFromUtc.Value);
        if (startToUtc.HasValue)
            predicate = CombineAnd(predicate, e => e.CreateTime <= startToUtc.Value);
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
