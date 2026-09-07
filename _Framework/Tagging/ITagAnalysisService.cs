using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签分析服务接口（V0.3.0 实现）——基于 <see cref="ITagHitStore"/> 落库数据提供聚合分析：
/// 按维度 / 时间范围 / 标签名统计命中频次、趋势、占比。
/// <para>V0.2.0 为占位契约；V0.3.0 补全实现。红线合规：实现委托 <see cref="TagHitRecordEntityDataService"/> 聚合业务方法。</para>
/// </summary>
public interface ITagAnalysisService
{
    /// <summary>高频标签 TopN（GROUP BY Dimension+TagName——按指定维度/时间范围过滤）。</summary>
    Task<List<TagFrequency>> GetFrequencyAsync(
        string? dimension, DateTime? from, DateTime? to, int topN, CancellationToken ct = default);

    /// <summary>标签趋势（按时间粒度分桶聚合）。</summary>
    Task<List<TagTrendPoint>> GetTrendAsync(
        string dimension, string? tagName, DateTime from, DateTime to, TagGranularity granularity, CancellationToken ct = default);

    /// <summary>维度分布（GROUP BY Dimension——占比分析）。</summary>
    Task<List<TagDimensionShare>> GetDimensionDistributionAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default);
}