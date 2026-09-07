using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Tagging;
using TKWF.Ext.Tagging.DTOs;

namespace TKWF.Ext.Tagging;

/// <summary>数据服务：标签规则实体（V0.3.0，Oracle P1-3 业务方法 partial）——规则查询方法供 ITagRuleStore 委托。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 TagRuleEntityDataService.g.cs 承载。
partial class TagRuleEntityDataService(IDomainUser user, IEntityDAC<TagRuleEntity> dac)
        : DomainDataServiceBase<TagRuleEntity, TagRuleEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>全量规则（按 Dimension + Priority 排序）——供 ITagService.LoadRules。</summary>
    public Task<List<TagRuleEntity>> GetAllOrderedAsync(CancellationToken ct = default)
        => EntitySelectAsync(r => true, 0, int.MaxValue,
            q => q.OrderBy(r => r.Dimension).ThenByDescending(r => r.Priority), ct);

    /// <summary>启用规则（LoadRules 过滤 IsEnabled）。</summary>
    public Task<List<TagRuleEntity>> GetEnabledAsync(CancellationToken ct = default)
        => EntitySelectAsync(r => r.IsEnabled, 0, int.MaxValue,
            q => q.OrderBy(r => r.Dimension).ThenByDescending(r => r.Priority), ct);

    /// <summary>按维度查询规则。</summary>
    public Task<List<TagRuleEntity>> GetByDimensionAsync(string dimension, CancellationToken ct = default)
        => EntitySelectAsync(r => r.Dimension == dimension, 0, int.MaxValue,
            q => q.OrderByDescending(r => r.Priority), ct);

    /// <summary>按业务键（Dimension+TagName+Pattern）查找唯一规则（幂等 upsert 用）。</summary>
    public Task<TagRuleEntity?> FindByBusinessKeyAsync(
        string dimension, string tagName, string pattern, CancellationToken ct = default)
        => EntityGetAsync(r => r.Dimension == dimension && r.TagName == tagName && r.Pattern == pattern, ct);

    /// <summary>按 Id 查实体（UpdateAsync 定位用）。</summary>
    public Task<TagRuleEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        => EntityGetAsync(r => r.Id == id, ct);

    /// <summary>幂等创建（业务键已存在返回既有；否则插入返回新实体）。</summary>
    public async Task<TagRuleEntity> CreateOrGetAsync(TagRuleEntity entity, CancellationToken ct = default)
    {
        var existing = await FindByBusinessKeyAsync(entity.Dimension, entity.TagName, entity.Pattern, ct);
        if (existing != null) return existing!;
        entity.CreateTime = DateTime.UtcNow;
        entity.UpdateTime = DateTime.UtcNow;
        return await EntityCreateAsync(entity, ct);
    }

    /// <summary>按业务键更新（不存在返回 null）。</summary>
    public async Task<TagRuleEntity?> UpdateByBusinessKeyAsync(TagRuleEntity entity, CancellationToken ct = default)
    {
        var existing = await FindByBusinessKeyAsync(entity.Dimension, entity.TagName, entity.Pattern, ct);
        if (existing == null) return null;
        entity.Id = existing.Id;
        entity.CreateTime = existing.CreateTime;
        entity.UpdateTime = DateTime.UtcNow;
        return await EntityUpdateAsync(entity, ct);
    }

    /// <summary>按 Id 删除（hasSoftDelete:false → 物理删，经 DeleteAsync 分派）。</summary>
    public Task<bool> DeleteByIdAsync(long id, CancellationToken ct = default)
        => DeleteAsync(id, ct);
}