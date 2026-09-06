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

partial class DictionaryDefinitionEntityDataService(IDomainUser user, IEntityDAC<DictionaryDefinitionEntity> dac)
    : DomainDataServiceBase<DictionaryDefinitionEntity, DictionaryDefinitionEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线整改（2026-09-07）：DictionaryStore 委托路径的业务方法 ──

    /// <summary>按编码查询字典定义。</summary>
    public async Task<DictionaryDefinitionEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await EntityGetAsync(d => d.Code == code, ct);

    /// <summary>按 Id 查询字典定义（实体返回——基类 GetByIdAsync 返回 DTO，故改名避冲突）。</summary>
    public async Task<DictionaryDefinitionEntity?> GetEntityByIdAsync(long id, CancellationToken ct = default)
        => await EntityGetAsync(d => d.Id == id, ct);

    /// <summary>分页查询字典定义（按 Id 倒序）。</summary>
    public async Task<List<DictionaryDefinitionEntity>> GetDefinitionsPagedAsync(int skip, int take, CancellationToken ct = default)
        => await EntitySelectAsync(null, skip, take, q => q.OrderByDescending(d => d.Id), ct);

    /// <summary>按 Code Upsert——存在则更新（保留自增 Id + CreateTime），不存在则插入。</summary>
    public async Task UpsertByCodeAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default)
    {
        var existing = await EntityGetAsync(d => d.Code == definition.Code, ct);
        if (existing != null)
        {
            definition.Id = existing.Id;
            definition.CreateTime = existing.CreateTime;
            definition.UpdateTime = DateTimeOffset.Now;
            await EntityUpdateAsync(definition, ct);
        }
        else
        {
            await EntityCreateAsync(definition, ct);
        }
    }

    /// <summary>按 Id 物理删除（hasSoftDelete:false；EntitySoftDeleteAsync 对未启用软删实体抛异常，故用 DeleteBatch）。</summary>
    public async Task DeleteEntityAsync(long id, CancellationToken ct = default)
    {
        var existing = await EntityGetAsync(d => d.Id == id, ct);
        if (existing != null)
            await EntityDeleteBatchAsync(new[] { id }, ct);
    }
}
