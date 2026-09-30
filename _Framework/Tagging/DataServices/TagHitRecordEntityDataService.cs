using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
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

    /// <summary>频次聚合（按维度/时间窗 → SQL GROUP BY 复合键 + COUNT + ORDER BY COUNT DESC + LIMIT topN 下推，V4.10.39 F7）。
    /// <para>实现说明（V0.4.3）：分组聚合经 <c>GroupCountAsync</c>（复合键 TopN 重载）走 FreeSql ISelect.GroupBy →
    /// GROUP BY 复合键( Dimension, TagName ) + COUNT + ORDER BY COUNT DESC + LIMIT——全下推 SQL，
    /// 无内存 GroupBy / 无 100_000 行拉取上限。</para>
    /// <para>空键语义：原内存实现未显式跳过 null 键（SQL GROUP BY 会将其并为 null 分组）——保持原语义不做过滤；
    /// TagHitRecordEntityDimension/TagName 写入侧均有默认兜底（DefaultTagProcessor），实际 null 键罕见。</para></summary>
    public async Task<List<TagFrequency>> GetFrequencyAsync(
        string? dimension, DateTime? from, DateTime? to, int topN, CancellationToken ct = default)
    {
        var query = QueryForUser();
        if (dimension != null) query = query.Where(h => h.Dimension == dimension);
        if (from.HasValue) query = query.Where(h => h.HitTime >= from.Value);
        if (to.HasValue) query = query.Where(h => h.HitTime <= to.Value);
        var pairs = await query.GroupCountAsync(h => new { h.Dimension, h.TagName }, topN, ct);
        return pairs
            .Select(p => new TagFrequency(p.Key.Dimension, p.Key.TagName, p.Value))
            .ToList();
    }

    /// <summary>趋势聚合（按时间粒度分桶）——<b>保留内存分桶路径（V0.4.3 决策：方案 B）</b>。
    /// <para>为何不 SQL 下推（F7 只替换可翻译部分，框架审核报告 §六标注"BucketKey 内存 helper 须改 SQL 可翻译表达式"为
    /// 注意点而非强制）：① 分桶键 <see cref="TaggingAggregations.BucketKey"/> 按 <paramref name="granularity"/> 运行时
    /// switch（含 Week 周一零时 <c>((int)DayOfWeek + 6) % 7</c> 取模 / 月末 TypeKind 处理），无法在单个静态表达式树中
    /// 表达 —— 需按粒度动态合成 keySelector，复杂度高且 Week 的周一偏移无法翻译为 SQL；② 框架实证仅覆盖
    /// <c>strftime('%Y')</c>（Year 桶），Hour/Day/Month 的 &lt;Y,M,D,H&gt; 复合键 + 内存重建起点语法可行但 Week 破坏语义；③
    /// 趋势需按 TimeBucket 升序跨 TagName 交织——GROUP BY(TagName, bucket) 的 SQL 键序与 OrderBy(TimeBucket) 语义不一致。
    /// 故保留内存 GroupBy + <c>Take(100_000)</c> 上限（V0.3.0 P2-3 先落地后优化，命中量可控；趋势窗口命中通常仅数千行）。</para></summary>
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

    /// <summary>维度分布（GROUP BY Dimension → ORDER BY COUNT DESC 全下推）——同上，V4.10.39 F7。</summary>
    public async Task<List<TagDimensionShare>> GetDimensionDistributionAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var query = QueryForUser();
        if (from.HasValue) query = query.Where(h => h.HitTime >= from.Value);
        if (to.HasValue) query = query.Where(h => h.HitTime <= to.Value);
        var pairs = await query.GroupByAsync(
            h => h.Dimension,
            g => g.Count(),
            descending: true,
            topN: null,
            g => new KeyValuePair<string, long>(g.Key, g.Count()),
            ct);
        return pairs
            .Select(p => new TagDimensionShare(p.Key, p.Value))
            .ToList();
    }
}