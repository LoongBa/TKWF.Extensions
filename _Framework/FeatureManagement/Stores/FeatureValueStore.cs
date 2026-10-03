using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 值存储实现（internal sealed）——经 <see cref="FeatureValueEntityDataService"/> 委托持久化
/// （数据访问红线合规：不注入 IFreeSql/IEntityDAC，只依赖 DataService）。
/// <para>读路径静默（异常 → null/空，fail-closed 降级）；写路径异常传播（管理改值失败必须告知）。</para>
/// <para>V4.10.53（领域自治根治，正确路线）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c>
/// 获取用户上下文（<b>IDomainUser 永不注册 DI</b>，D01；旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败——
/// v0.3.3 同根缺陷）；<c>[DiContractIgnore]</c> 豁免 DI001（AddConstructibleService 运行时手写注册非 SG DI 契约目标）；
/// DataService 经基类 <c>User.Use&lt;T&gt;()</c> 懒加载（DI004 零豁免）；注册形态改
/// <c>AddConstructibleService&lt;IFeatureValueStore, FeatureValueStore&gt;</c>（接口可构造守卫工厂 + 实现类 throw-factory）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class FeatureValueStore : DomainServiceBase, IFeatureValueStore
{
    private readonly ILogger<FeatureValueStore> _logger;
    private FeatureValueEntityDataService? _dataService;

    private FeatureValueEntityDataService DataService => _dataService ??= User.Use<FeatureValueEntityDataService>();

    public FeatureValueStore(IDomainUser user, ILogger<FeatureValueStore> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FeatureValueEntity?> GetAsync(
        string name, string providerName, string? providerKey, CancellationToken ct = default)
    {
        try
        {
            return await DataService.GetByKeyAsync(name, providerName, providerKey, ct);
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
            return await DataService.GetListAsync(providerName, providerKey, 0, 100000, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Feature 值列表读取失败（静默降级）：{Provider}/{Key}", providerName, providerKey);
            return Array.Empty<FeatureValueEntity>();
        }
    }

    public Task SetAsync(FeatureValueEntity entity, CancellationToken ct = default)
        => DataService.UpsertByKeyAsync(entity, ct);   // 写传播（唯一约束/DB 异常自然传播）

    public async Task DeleteAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        => await DataService.DeleteByKeyAsync(name, providerName, providerKey, ct);   // 写传播
}
