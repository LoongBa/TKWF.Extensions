using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 值存储实现（internal sealed）——经 <see cref="FeatureValueEntityDataService"/> 委托持久化
/// （数据访问红线合规：不注入 IFreeSql/IEntityDAC，只依赖 DataService）。
/// <para>读路径静默（异常 → null/空，fail-closed 降级）；写路径异常传播（管理改值失败必须告知）。</para>
/// </summary>
internal sealed class FeatureValueStore : IFeatureValueStore
{
    private readonly FeatureValueEntityDataService _dataService;
    private readonly ILogger<FeatureValueStore> _logger;

    public FeatureValueStore(FeatureValueEntityDataService dataService, ILogger<FeatureValueStore> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FeatureValueEntity?> GetAsync(
        string name, string providerName, string? providerKey, CancellationToken ct = default)
    {
        try
        {
            return await _dataService.GetByKeyAsync(name, providerName, providerKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feature 值读取失败（静默降级）：{Name}/{Provider}/{Key}", name, providerName, providerKey);
            return null;
        }
    }

    public async Task<IReadOnlyList<FeatureValueEntity>> GetListAsync(
        string? providerName, string? providerKey, CancellationToken ct = default)
    {
        try
        {
            return await _dataService.GetListAsync(providerName, providerKey, 0, 100000, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feature 值列表读取失败（静默降级）：{Provider}/{Key}", providerName, providerKey);
            return Array.Empty<FeatureValueEntity>();
        }
    }

    public Task SetAsync(FeatureValueEntity entity, CancellationToken ct = default)
        => _dataService.UpsertByKeyAsync(entity, ct);   // 写传播（唯一约束/DB 异常自然传播）

    public async Task DeleteAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        => await _dataService.DeleteByKeyAsync(name, providerName, providerKey, ct);   // 写传播
}
