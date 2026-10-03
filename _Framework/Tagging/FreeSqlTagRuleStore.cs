using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.Tags;

namespace TKWF.Ext.Tagging;

/// <summary>
/// FreeSql 标签规则存储（V0.3.0）——委托 <see cref="TagRuleEntityDataService"/>（SG1 DataService）持久化，
/// 遵循数据访问红线（2026-09-07 用户裁定）：不直接注入 IFreeSql / IEntityDAC。
/// <para>DTO ↔ <see cref="TagRule"/>（Utility 算法模型）映射：Store I/O 即算法 I/O，零逻辑转换。</para>
/// <para>异常静默对齐既有扩展：查询失败返回空，写入失败记录 Warning。</para>
/// <para>V4.10.53（领域自治根治）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI，旧 TryAddScoped 构造注入 IDomainUser 生产必失败）；注册形态改
/// <c>AddConstructibleService&lt;ITagRuleStore, FreeSqlTagRuleStore&gt;</c>（接口可构造守卫工厂）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class FreeSqlTagRuleStore : DomainServiceBase, ITagRuleStore
{
    private TagRuleEntityDataService? _dataService;
    private readonly ILogger<FreeSqlTagRuleStore> _logger;

    private TagRuleEntityDataService DataService => _dataService ??= User.Use<TagRuleEntityDataService>();

    public FreeSqlTagRuleStore(IDomainUser user, ILogger<FreeSqlTagRuleStore> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<TagRule>> GetAllAsync(CancellationToken ct = default)
    {
        try
        {
var entities = await DataService.GetAllOrderedAsync(ct);
            return entities.Select(ToModel).ToList();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签规则全量读取失败"); return []; }
    }

    public async Task<List<TagRule>> GetEnabledAsync(CancellationToken ct = default)
    {
        try
        {
            var entities = await DataService.GetEnabledAsync(ct);
            return entities.Select(ToModel).ToList();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签启用规则读取失败"); return []; }
    }

    /// <summary>创建规则（幂等：同 (Dimension,TagName,Pattern) 已存在返回既有 Id）——经 CreateOrGetAsync。</summary>
    public async Task<long?> CreateAsync(TagRule rule, CancellationToken ct = default)
    {
        try
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Dimension)
                || string.IsNullOrWhiteSpace(rule.TagName) || string.IsNullOrWhiteSpace(rule.Pattern))
                return null;

            var entity = ToEntity(rule, new TagRuleEntity());
            var result = await DataService.CreateOrGetAsync(entity, ct);
            return result.Id > 0 ? result.Id : null;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签规则创建失败: {Tag}", rule?.TagName); return null; }
    }

    /// <summary>更新规则（按业务键定位；不存在返回 false）。</summary>
    public async Task<bool> UpdateAsync(TagRule rule, CancellationToken ct = default)
    {
        try
        {
            if (rule is null) return false;
            var entity = ToEntity(rule, new TagRuleEntity());
            var result = await DataService.UpdateByBusinessKeyAsync(entity, ct);
            return result != null;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签规则更新失败: {Tag}", rule?.TagName); return false; }
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        try
        {
            return await DataService.DeleteByIdAsync(id, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签规则删除失败: Id={Id}", id); return false; }
    }

    public async Task<List<TagRule>> GetByDimensionAsync(string dimension, CancellationToken ct = default)
    {
        try
        {
            var entities = await DataService.GetByDimensionAsync(dimension, ct);
            return entities.Select(ToModel).ToList();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "标签规则按维度读取失败: {Dim}", dimension); return []; }
    }

    // ── Entity ↔ TagRule 映射 ──

    private static TagRule ToModel(TagRuleEntity e) => new()
    {
        Dimension = e.Dimension,
        TagName = e.TagName,
        MatchMode = (TagMatchMode)e.MatchMode,
        Pattern = e.Pattern,
        IsEnabled = e.IsEnabled,
        Priority = e.Priority,
        ExclusionGroup = e.ExclusionGroup,
        IsDefaultRule = e.IsDefaultRule,
        DefaultTagName = e.DefaultTagName
    };

    private static TagRuleEntity ToEntity(TagRule rule, TagRuleEntity entity)
    {
        entity.Dimension = rule.Dimension;
        entity.TagName = rule.TagName;
        entity.MatchMode = (int)rule.MatchMode;
        entity.Pattern = rule.Pattern;
        entity.IsEnabled = rule.IsEnabled;
        entity.Priority = rule.Priority;
        entity.ExclusionGroup = rule.ExclusionGroup;
        entity.IsDefaultRule = rule.IsDefaultRule;
        entity.DefaultTagName = rule.DefaultTagName;
        return entity;
    }
}
