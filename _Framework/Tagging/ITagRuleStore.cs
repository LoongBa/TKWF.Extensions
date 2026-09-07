using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Utility.Tags;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签规则存储接口（V0.3.0 实现）——规则经 <see cref="TagRuleEntity"/>（SG1 实体，表 <c>TagRule</c>）持久化，
/// 支持管理端维护（维度/匹配模式/模式串/启用/优先级/互斥组/默认标签）。
/// <para>V0.2.0 为占位契约（ADR52：Tag 算法回归 TKWF.Utility + Ext 瘦身为存储扩展）；V0.3.0 补全实现。</para>
/// <para>红线合规：实现委托 <see cref="TagRuleEntityDataService"/>（SG1 DataService），禁裸 ORM。</para>
/// </summary>
public interface ITagRuleStore
{
    /// <summary>全量规则（按 Dimension + Priority 排序）——供 <c>ITagService.LoadRules</c>。</summary>
    Task<List<TagRule>> GetAllAsync(CancellationToken ct = default);

    /// <summary>启用规则（LoadRules 过滤 IsEnabled）。</summary>
    Task<List<TagRule>> GetEnabledAsync(CancellationToken ct = default);

    /// <summary>创建规则（幂等：同 (Dimension,TagName,Pattern) 已存在返回既有 Id；成功返回新 Id）。</summary>
    Task<long?> CreateAsync(TagRule rule, CancellationToken ct = default);

    /// <summary>更新规则（按业务键 (Dimension+TagName+Pattern) 定位；Pattern 变更需删后重建——业务键含 Pattern；成功返回 true）。</summary>
    Task<bool> UpdateAsync(TagRule rule, CancellationToken ct = default);

    /// <summary>删除规则（按 Id；成功返回 true）。</summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>按维度查询规则。</summary>
    Task<List<TagRule>> GetByDimensionAsync(string dimension, CancellationToken ct = default);
}