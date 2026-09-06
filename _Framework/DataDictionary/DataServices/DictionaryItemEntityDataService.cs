using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.DataDictionary;
using TKWF.Ext.DataDictionary.DTOs;

namespace TKWF.Ext.DataDictionary;

partial class DictionaryItemEntityDataService(IDomainUser user, IEntityDAC<DictionaryItemEntity> dac)
    : DomainDataServiceBase<DictionaryItemEntity, DictionaryItemEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线整改（2026-09-07）：DictionaryStore 委托路径的业务方法 ──

    /// <summary>按 Id 查询字典项（实体返回——基类 GetByIdAsync 返回 DTO，故改名避冲突）。</summary>
    public async Task<DictionaryItemEntity?> GetEntityByIdAsync(long id, CancellationToken ct = default)
        => await EntityGetAsync(i => i.Id == id, ct);

    /// <summary>按 DefinitionId 查询启用的字典项（按 Order 升序）。</summary>
    public async Task<List<DictionaryItemEntity>> GetItemsByDefinitionIdAsync(long definitionId, CancellationToken ct = default)
        => await EntitySelectAsync(
            i => i.DefinitionId == definitionId && i.IsEnabled,
            0, 1000,
            q => q.OrderBy(i => i.Order), ct);

    /// <summary>按 DefinitionId + Code Upsert——存在则更新（保留自增 Id + CreateTime），不存在则插入。</summary>
    public async Task UpsertByKeyAsync(DictionaryItemEntity item, CancellationToken ct = default)
    {
        var existing = await EntityGetAsync(
            i => i.DefinitionId == item.DefinitionId && i.Code == item.Code, ct);
        if (existing != null)
        {
            item.Id = existing.Id;
            item.CreateTime = existing.CreateTime;
            item.UpdateTime = DateTimeOffset.Now;
            await EntityUpdateAsync(item, ct);
        }
        else
        {
            await EntityCreateAsync(item, ct);
        }
    }

    /// <summary>按 DefinitionId 批量删除（级联——DeleteDefinition 先删项）。</summary>
    public async Task DeleteItemsByDefinitionIdAsync(long definitionId, CancellationToken ct = default)
    {
        var items = await EntitySelectAsync(i => i.DefinitionId == definitionId, 0, 10000, ct: ct);
        if (items.Count > 0)
            await EntityDeleteBatchAsync(items.Select(i => i.Id), ct);
    }

    /// <summary>按 Id 物理删除（hasSoftDelete:false；EntitySoftDeleteAsync 对未启用软删实体抛异常，故用 DeleteBatch）。</summary>
    public async Task DeleteEntityAsync(long id, CancellationToken ct = default)
    {
        var existing = await EntityGetAsync(i => i.Id == id, ct);
        if (existing != null)
            await EntityDeleteBatchAsync(new[] { id }, ct);
    }
}
