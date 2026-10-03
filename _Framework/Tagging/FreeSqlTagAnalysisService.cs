using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Tagging;

/// <summary>
/// FreeSql 标签分析服务（V0.3.0）——纯委托 <see cref="TagHitRecordEntityDataService"/> 聚合业务方法
/// （Oracle P1-3：聚合在 DataService 内 GroupBy（SQL 级），Store 只调 public 方法——红线合规，不碰 internal Query）。
/// <para>聚合语义：频次 TopN / 趋势分桶 / 维度占比——基于落库命中数据。</para>
/// <para>异常静默对齐既有扩展：查询失败返回空。</para>
/// <para>V4.10.53（领域自治根治）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；注册形态改 <c>AddConstructibleService</c>（接口可构造守卫工厂）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class FreeSqlTagAnalysisService : DomainServiceBase, ITagAnalysisService
{
    private TagHitRecordEntityDataService? _dataService;
    private readonly ILogger<FreeSqlTagAnalysisService> _logger;

    private TagHitRecordEntityDataService DataService => _dataService ??= User.Use<TagHitRecordEntityDataService>();

    public FreeSqlTagAnalysisService(IDomainUser user, ILogger<FreeSqlTagAnalysisService> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<TagFrequency>> GetFrequencyAsync(
        string? dimension, DateTime? from, DateTime? to, int topN, CancellationToken ct = default)
    {
        try
        {
            if (topN < 1) topN = 10;
            return await DataService.GetFrequencyAsync(dimension, from, to, topN, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签频次聚合失败"); return []; }
    }

    public async Task<List<TagTrendPoint>> GetTrendAsync(
        string dimension, string? tagName, DateTime from, DateTime to, TagGranularity granularity, CancellationToken ct = default)
    {
        try
        {
            return await DataService.GetTrendAsync(dimension, tagName, from, to, granularity, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签趋势聚合失败: {Dim}", dimension); return []; }
    }

    public async Task<List<TagDimensionShare>> GetDimensionDistributionAsync(
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        try
        {
            return await DataService.GetDimensionDistributionAsync(from, to, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签维度分布聚合失败"); return []; }
    }
}
