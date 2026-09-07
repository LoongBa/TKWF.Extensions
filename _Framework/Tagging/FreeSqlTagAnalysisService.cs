using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Tagging;

/// <summary>
/// FreeSql 标签分析服务（V0.3.0）——纯委托 <see cref="TagHitRecordEntityDataService"/> 聚合业务方法
/// （Oracle P1-3：聚合在 DataService 内 GroupBy（SQL 级），Store 只调 public 方法——红线合规，不碰 internal Query）。
/// <para>聚合语义：频次 TopN / 趋势分桶 / 维度占比——基于落库命中数据。</para>
/// <para>异常静默对齐既有扩展：查询失败返回空。</para>
/// </summary>
internal sealed class FreeSqlTagAnalysisService : ITagAnalysisService
{
    private readonly TagHitRecordEntityDataService _dataService;
    private readonly ILogger<FreeSqlTagAnalysisService> _logger;

    public FreeSqlTagAnalysisService(TagHitRecordEntityDataService dataService, ILogger<FreeSqlTagAnalysisService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<TagFrequency>> GetFrequencyAsync(
        string? dimension, DateTime? from, DateTime? to, int topN, CancellationToken ct = default)
    {
        try
        {
            if (topN < 1) topN = 10;
            return await _dataService.GetFrequencyAsync(dimension, from, to, topN, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签频次聚合失败"); return []; }
    }

    public async Task<List<TagTrendPoint>> GetTrendAsync(
        string dimension, string? tagName, DateTime from, DateTime to, TagGranularity granularity, CancellationToken ct = default)
    {
        try
        {
            return await _dataService.GetTrendAsync(dimension, tagName, from, to, granularity, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签趋势聚合失败: {Dim}", dimension); return []; }
    }

    public async Task<List<TagDimensionShare>> GetDimensionDistributionAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        try
        {
            return await _dataService.GetDimensionDistributionAsync(from, to, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签维度分布聚合失败"); return []; }
    }
}
