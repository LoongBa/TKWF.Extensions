using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// 测试 Store——模拟消费方 <see cref="IMetricResultStore"/> 实现（消费方模拟，红线合规）。
/// <para><b>红线（2026-09-07 裁定）</b>：本 Store 不注入 IFreeSql/IEntityDAC，仅经
/// <see cref="TestMetricResultEntityDataService"/> public 委托方法访问数据。</para>
/// <para>行→实体映射：Value 用 switch 三路径（null→null / string 直通 / 复杂对象 JsonSerializer.Serialize
/// 复用 <see cref="MetricResultMapper.JsonOptions"/>，对齐方案 §3.4 示例 ③）；实体→行：ValueText 原样返回
/// （存储态字符串，Query 返回标准化行 Value = 落库文本）。</para>
/// </summary>
public sealed class TestMetricResultStore : IMetricResultStore
{
    private readonly TestMetricResultEntityDataService _ds;

    /// <summary>构造消费方 Store。</summary>
    public TestMetricResultStore(TestMetricResultEntityDataService ds)
    {
        _ds = ds ?? throw new ArgumentNullException(nameof(ds));
    }

    /// <inheritdoc />
    public async Task<int> SaveAsync(IReadOnlyList<MetricResultRow> rows, CancellationToken ct = default)
    {
        if (rows == null || rows.Count == 0) return 0;

        var entities = rows.Select(r => new TestMetricResultEntity
        {
            SpecKey = r.SpecKey,
            Name = r.Name,
            // P3：Value 后引擎不变量为标量（decimal/double/string/null）——标量经 JsonSerializer 序列化（decimal→"12.5"），
            // string 直通，null→null；复杂对象 JSON 化（防御——契约保留 object? 容纳自定义计算器）。
            ValueText = r.Value switch
            {
                null => null,
                string s => s,
                _ => JsonSerializer.Serialize(r.Value, MetricResultMapper.JsonOptions)
            },
            Unit = r.Unit,
            DimensionsJson = r.DimensionsJson,
            CalculatedAtUtc = r.CalculatedAtUtc
        }).ToList();

        await _ds.CreateBatchAsync(entities, ct);
        return entities.Count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricResultRow>> QueryAsync(MetricResultQuery query, CancellationToken ct = default)
    {
        var (skip, take) = NormalizePage(query.Skip, query.Take);
        // P4：Take<0 → 0 行（NormalizePage 归零）。⚠️ FreeSql `.Take(0)` 语义为"不限制"（返回全量）而非空集——
        // 须提前短路返回空集，不能把 take=0 传给 DataService。
        if (take <= 0) return [];

        var entities = await _ds.QueryByMetricAsync(
            query.SpecKey, query.Name, query.FromUtc, query.ToUtc, skip, take, ct);

        return entities.Select(MapToRow).ToList();
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(MetricResultQuery query, CancellationToken ct = default)
        => await _ds.CountByMetricAsync(
            query.SpecKey, query.Name, query.FromUtc, query.ToUtc, ct);

    /// <inheritdoc />
    public Task<int> CleanupExpiredAsync(int retentionDays, int batchSize = 500, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        return _ds.DeleteBeforeAsync(cutoff, batchSize, ct);
    }

    /// <summary>
    /// 分页规范化——Skip 下限 0；Take 静默钳制 [1,200]（P4）：超界 → 200；Take&lt;0 → 0（返回空集，调用方短路）；
    /// Take=0 → 50（消费方选择——视为"使用默认值"，因契约"钳制到 [1,200]"可被解读为 Take=0 → 1，此处选默认更符合直觉）。
    /// </summary>
    private static (int Skip, int Take) NormalizePage(int skip, int take)
    {
        skip = Math.Max(0, skip);
        if (take < 0) return (skip, 0);
        take = Math.Min(take, 200);
        if (take < 1) take = 50;
        return (skip, take);
    }

    /// <summary>实体 → 标准化行（Value = ValueText 存储态文本；SpecKey/DimensionsJson 透传）。</summary>
    private static MetricResultRow MapToRow(TestMetricResultEntity e)
        => new(e.SpecKey, e.Name, e.ValueText, e.Unit, e.DimensionsJson, e.CalculatedAtUtc);
}