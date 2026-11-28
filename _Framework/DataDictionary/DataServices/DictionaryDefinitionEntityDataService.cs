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

    // ── A5 Store-debt cleanup：IDictionaryStore/DictionaryStore 已删除（DataService 薄包装伪层）──
    // 原 Store 的组合方法收编为本 DataService 分部业务方法（User.Use<> 懒加载其它 DataService），
    // 消费方与测试改经 User.Use<DictionaryDefinitionEntityDataService>() 单一入口，调用签名不变。

    private DictionaryItemEntityDataService? _itemDs;
    private DictionaryItemViewDataService? _viewDs;
    private DictionaryItemEntityDataService ItemDs => _itemDs ??= User.Use<DictionaryItemEntityDataService>();
    private DictionaryItemViewDataService ViewDs => _viewDs ??= User.Use<DictionaryItemViewDataService>();

    /// <summary>按编码读取字典定义。</summary>
    public Task<DictionaryDefinitionEntity?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default)
        => GetByCodeAsync(code, ct);

    /// <summary>按 Id 读取字典定义。</summary>
    public Task<DictionaryDefinitionEntity?> GetDefinitionByIdAsync(long id, CancellationToken ct = default)
        => GetEntityByIdAsync(id, ct);

    /// <summary>按 Id 读取字典项。</summary>
    public Task<DictionaryItemEntity?> GetItemByIdAsync(long id, CancellationToken ct = default)
        => ItemDs.GetEntityByIdAsync(id, ct);

    /// <summary>分页读取字典定义列表。</summary>
    public async Task<IReadOnlyList<DictionaryDefinitionEntity>> GetDefinitionsAsync(int skip = 0, int take = 20, CancellationToken ct = default)
        => await GetDefinitionsPagedAsync(skip, take, ct);

    /// <summary>读取指定字典定义的所有项。</summary>
    public async Task<IReadOnlyList<DictionaryItemEntity>> GetItemsAsync(long definitionId, CancellationToken ct = default)
        => await ItemDs.GetItemsByDefinitionIdAsync(definitionId, ct);

    /// <summary>按定义编码读取项（VEntity JOIN 视图）。</summary>
    public Task<List<DictionaryItemView>> GetItemsByDefinitionCodeAsync(string code, CancellationToken ct = default)
        => ViewDs.GetByDefinitionCodeAsync(code, ct);

    /// <summary>新增或更新字典定义（按 Code 定位，null 静默跳过）。</summary>
    public Task UpsertDefinitionAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default)
        => definition == null ? Task.CompletedTask : UpsertByCodeAsync(definition, ct);

    /// <summary>新增或更新字典项（按 DefinitionId+Code 定位，null 静默跳过）。</summary>
    public Task UpsertItemAsync(DictionaryItemEntity item, CancellationToken ct = default)
        => item == null ? Task.CompletedTask : ItemDs.UpsertByKeyAsync(item, ct);

    /// <summary>删除字典定义（先级联删项）。</summary>
    public async Task DeleteDefinitionAsync(long id, CancellationToken ct = default)
    {
        await ItemDs.DeleteItemsByDefinitionIdAsync(id, ct);
        await DeleteEntityAsync(id, ct);
    }

    /// <summary>删除字典项。</summary>
    public Task DeleteItemAsync(long id, CancellationToken ct = default)
        => ItemDs.DeleteEntityAsync(id, ct);
}
