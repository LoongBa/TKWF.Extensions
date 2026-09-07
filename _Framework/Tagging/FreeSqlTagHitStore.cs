using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Utility.Tags;
using TKWF.Ext.Tagging.DTOs;

namespace TKWF.Ext.Tagging;

/// <summary>
/// FreeSql 标签命中存储（V0.3.0）——委托 <see cref="TagHitRecordEntityDataService"/>（SG1 DataService）批量落库 + 查询，
/// 遵循数据访问红线（2026-09-07 用户裁定）：不直接注入 IFreeSql / IEntityDAC。
/// <para>批量落库经 DataService <c>EntityCreateBatchAsync</c>（单事务）；DTO ↔ <see cref="TagHit"/> 映射零逻辑。</para>
/// <para>异常静默对齐既有扩展：查询失败返回空，写入失败记录 Warning。</para>
/// </summary>
internal sealed class FreeSqlTagHitStore : ITagHitStore
{
    private readonly TagHitRecordEntityDataService _dataService;
    private readonly ILogger<FreeSqlTagHitStore> _logger;

    public FreeSqlTagHitStore(TagHitRecordEntityDataService dataService, ILogger<FreeSqlTagHitStore> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecordHitsAsync(
IEnumerable<TagHit> hits, string? sourceText = null, DateTime? hitTime = null, CancellationToken ct = default)
    {
        var hitList = hits?.ToList() ?? [];   // P2-6：try 前物化——避免 catch 重枚举惰性序列
        try
        {
            var rows = hitList.Select(h => new TagHitRecordEntity
            {
                Dimension = h.Dimension,
                TagName = h.TagName,
                MatchedValue = h.MatchedValue,
                StartIndex = h.StartIndex,
                Length = h.Length,
                Priority = h.Priority,
                ExclusionGroup = h.ExclusionGroup,
                SourceText = sourceText,
                HitTime = hitTime ?? DateTime.UtcNow
            }).ToList();
            if (rows.Count == 0) return;
            await _dataService.RecordHitsAsync(rows, ct);   // 批量单事务（P2-1）
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签命中批量落库失败: {Count} 条", hitList.Count); }
    }

    public async Task<List<TagHit>> GetByDimensionAsync(
        string dimension, DateTime? from, DateTime? to, int skip, int take, CancellationToken ct = default)
    {
        try
        {
var entities = await _dataService.GetByDimensionAsync(dimension, from, to, skip, take, ct);
            return entities.Select(ToModel).ToList();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签命中查询失败: {Dim}", dimension); return []; }
    }

    public async Task<List<TagHit>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        try
        {
            if (limit < 1) limit = 50;
            var entities = await _dataService.GetRecentAsync(limit, ct);
            return entities.Select(ToModel).ToList();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "最近命中查询失败"); return []; }
    }

    // ── Entity ↔ TagHit 映射 ──

    private static TagHit ToModel(TagHitRecordEntity e) => new(
        e.Dimension, e.TagName, e.MatchedValue, e.StartIndex, e.Length, e.Priority, e.ExclusionGroup);
}
