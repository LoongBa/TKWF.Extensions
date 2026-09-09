using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.FeatureManagement.DTOs;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Feature 值 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para><b>管理 API 约束（C3 评审裁定）</b>：本 DataService 仅内部存储（不标 <c>[GenerateController]</c>）；
/// 管理接口经 <see cref="FeatureManagementApiService"/>（[GenerateController] + 写路径委托 IFeatureManager——
/// 缓存失效 + Global 唯一性统一处理）。裸 CRUD 禁止直通控制器（绕过预检与失效）。</para></summary>
public partial class FeatureValueEntityDataService(IDomainUser user, IEntityDAC<FeatureValueEntity> dac)
    : DomainDataServiceBase<FeatureValueEntity, FeatureValueEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>按 Name+Provider 读值（Manager 分层解析用）。</summary>
    public Task<FeatureValueEntity?> GetByKeyAsync(
        string name, string providerName, string? providerKey, CancellationToken ct = default)
        => EntityGetAsync(e => e.Name == name && e.ProviderName == providerName && e.ProviderKey == providerKey, ct);

    /// <summary>管理查询：按 Provider 层/键过滤（可空 = 全量）。</summary>
    public Task<List<FeatureValueEntity>> GetListAsync(
        string? providerName, string? providerKey, int skip, int take, CancellationToken ct = default)
    {
        System.Linq.Expressions.Expression<Func<FeatureValueEntity, bool>> predicate = e => true;
        if (providerName != null)
            predicate = CombineAnd(predicate, e => e.ProviderName == providerName);
        if (providerKey != null)
            predicate = CombineAnd(predicate, e => e.ProviderKey == providerKey);
        return EntitySelectAsync(predicate, skip, take, q => q.OrderBy(e => e.Name).ThenBy(e => e.ProviderName).ThenBy(e => e.Id), ct);
    }

    /// <summary>Upsert：存在更新/不存在创建（Manager 事务包裹内调用——Global 层唯一性由 Manager 应用层保证；
    /// 仅内部存储，管理接口经 FeatureManagementApiService）。</summary>
    public async Task UpsertByKeyAsync(FeatureValueEntity entity, CancellationToken ct = default)
    {
        var existing = await GetByKeyAsync(entity.Name, entity.ProviderName, entity.ProviderKey, ct);
        if (existing != null)
        {
            entity.Id = existing.Id;
            entity.CreateTime = existing.CreateTime;
            entity.UpdateTime = DateTime.UtcNow;
            await EntityUpdateAsync(entity, ct);
        }
        else
        {
            await EntityCreateAsync(entity, ct);
        }
    }

    /// <summary>按 Name+Provider 物理删除（仅内部存储，管理接口经 FeatureManagementApiService——缓存失效在 Manager）。</summary>
    public async Task DeleteByKeyAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
    {
        var existing = await GetByKeyAsync(name, providerName, providerKey, ct);
        if (existing != null)
            await EntityDeleteBatchAsync(new[] { existing.Id }, ct);
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> CombineAnd<T>(
        System.Linq.Expressions.Expression<Func<T, bool>> left,
        System.Linq.Expressions.Expression<Func<T, bool>> right)
    {
        var param = System.Linq.Expressions.Expression.Parameter(typeof(T));
        var body = System.Linq.Expressions.Expression.AndAlso(
            System.Linq.Expressions.Expression.Invoke(left, param),
            System.Linq.Expressions.Expression.Invoke(right, param));
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, param);
    }
}
