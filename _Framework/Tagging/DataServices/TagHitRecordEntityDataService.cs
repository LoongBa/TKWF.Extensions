using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Tagging;
using TKWF.Ext.Tagging.DTOs;

namespace TKWF.Ext.Tagging;

/// <summary>数据服务：标签命中记录实体（V0.3.0，Oracle P1-3 聚合业务方法 partial）——批量落库 + 分页 + 聚合分析，供 ITagHitStore/ITagAnalysisService 委托。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 TagHitRecordEntityDataService.g.cs 承载。
partial class TagHitRecordEntityDataService(IDomainUser user, IEntityDAC<TagHitRecordEntity> dac)
        : DomainDataServiceBase<TagHitRecordEntity, TagHitRecordEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>批量落库（P2-1 定稿：EntityCreateBatchAsync——基类批量插入，单事务）。</summary>
    public Task<List<TagHitRecordEntity>> RecordHitsAsync(IEnumerable<TagHitRecordEntity> rows, CancellationToken ct = default)
        => EntityCreateBatchAsync(rows, ct);

    /// <summary>按维度分页查询命中明细（时间范围过滤，倒序）。</summary>
    public Task<List<TagHitRecordEntity>> GetByDimensionAsync(
        string dimension, DateTime? from, DateTime? to, int skip, int take, CancellationToken ct = default)
    {
        System.Linq.Expressions.Expression<Func<TagHitRecordEntity, bool>> predicate = (from, to) switch
        {
            (null, null) => h => h.Dimension == dimension,
            (null, _) => h => h.Dimension == dimension && h.HitTime <= to,
            (_, null) => h => h.Dimension == dimension && h.HitTime >= from,
            _ => h => h.Dimension == dimension && h.HitTime >= from && h.HitTime <= to
        };
        return EntitySelectAsync(predicate, skip, take, q => q.OrderByDescending(h => h.HitTime), ct);
    }

    /// <summary>最近命中（默认 50）。</summary>
    public Task<List<TagHitRecordEntity>> GetRecentAsync(int limit, CancellationToken ct = default)
        => EntitySelectAsync(h => true, 0, limit, q => q.OrderByDescending(h => h.HitTime), ct);

    /// <summary>频次聚合（按维度/时间过滤 → 内存 GroupBy TopN）——Oracle P1-3：DataService 内聚合，Store 只委托。
    /// <para>实现说明（P2-3）：DAC 接口边界（ToListAsync&lt;TResult&gt; 需实体 query）→ SQL WHERE 过滤下推 + 内存 GroupBy；
    /// 行数上限 100_000 防全表拉取（P2-1）；命中量可控时先落地后优化。</para></summary>
    public async Task<List<TagFrequency>> GetFrequencyAsync(
        string? dimension, DateTime? from, DateTime? to, int topN, CancellationToken ct = default)
    {
        var query = QueryForUser();
        if (dimension != null) query = query.Where(h => h.Dimension == dimension);
        if (from.HasValue) query = query.Where(h => h.HitTime >= from.Value);
        if (to.HasValue) query = query.Where(h => h.HitTime <= to.Value);
        var rows = await Dac.ToListAsync(query.Take(100_000), ct);   // P2-1：安全上限防全表拉取
        return rows
            .GroupBy(h => new { h.Dimension, h.TagName })
            .Select(g => new TagFrequency(g.Key.Dimension, g.Key.TagName, g.LongCount()))
            .OrderByDescending(f => f.Count)
            .Take(topN)
            .ToList();
    }

    /// <summary>趋势聚合（按时间粒度分桶）——同上：SQL WHERE 下推 + 内存分桶（P2-3）。</summary>
    public async Task<List<TagTrendPoint>> GetTrendAsync(
        string dimension, string? tagName, DateTime from, DateTime to, TagGranularity granularity, CancellationToken ct = default)
    {
        var query = QueryForUser().Where(h => h.Dimension == dimension && h.HitTime >= from && h.HitTime <= to);
        if (tagName != null) query = query.Where(h => h.TagName == tagName);
        var rows = await Dac.ToListAsync(query.Take(100_000), ct);   // P2-1：安全上限
        return rows
            .GroupBy(h => new { h.TagName, Bucket = TaggingAggregations.BucketKey(h.HitTime, granularity) })
            .Select(g => new TagTrendPoint(g.Key.Bucket, g.Key.TagName, g.LongCount()))
            .OrderBy(t => t.TimeBucket)
            .ToList();
    }

    /// <summary>维度分布（GROUP BY Dimension）——同上：SQL WHERE 下推 + 内存 GroupBy（P2-3）。</summary>
    public async Task<List<TagDimensionShare>> GetDimensionDistributionAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var query = QueryForUser();
        if (from.HasValue) query = query.Where(h => h.HitTime >= from.Value);
        if (to.HasValue) query = query.Where(h => h.HitTime <= to.Value);
        var rows = await Dac.ToListAsync(query.Take(100_000), ct);   // P2-1：安全上限
        return rows
            .GroupBy(h => h.Dimension)
            .Select(g => new TagDimensionShare(g.Key, g.LongCount()))
            .OrderByDescending(s => s.Count)
            .ToList();
    }
}