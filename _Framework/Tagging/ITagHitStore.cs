using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Utility.Tags;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签命中存储接口（V0.3.0 实现）——命中结果经 <see cref="TagHitRecordEntity"/>（SG1 实体，表 <c>TagHit</c>）落库，
/// 记录维度/标签/位置/原文快照/时间戳，支撑"高频标签/时间分布/维度占比"数据分析。
/// <para>V0.2.0 为占位契约；V0.3.0 补全实现。红线合规：实现委托 <see cref="TagHitRecordEntityDataService"/>。</para>
/// </summary>
public interface ITagHitStore
{
    /// <summary>批量落库命中结果（单事务）；<paramref name="hitTime"/> 为空时用当前 UTC（回填场景可指定）。</summary>
    Task RecordHitsAsync(IEnumerable<TagHit> hits, string? sourceText = null, DateTime? hitTime = null, CancellationToken ct = default);

    /// <summary>按维度分页查询命中明细（时间范围过滤，倒序）。</summary>
    Task<List<TagHit>> GetByDimensionAsync(
        string dimension, DateTime? from, DateTime? to, int skip, int take, CancellationToken ct = default);

    /// <summary>最近命中（默认 50）。</summary>
    Task<List<TagHit>> GetRecentAsync(int limit, CancellationToken ct = default);
}