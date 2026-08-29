using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Tagging.Processors;

/// <summary>
/// 默认标签处理器：为没有命中任何规则的维度添加默认标签
/// </summary>
public class DefaultTagProcessor : ITagPipelinePostProcessor
{
    public void Process(List<TagHit> hits, IReadOnlyList<TagRule> contextRules)
    {
        // 从当前上下文中筛选出默认规则（因为在 Pipeline 层已经过滤过 IsEnabled，这里只需判断 IsDefaultRule）
        var defaultRules = contextRules.Where(r => r.IsDefaultRule).ToList();
        if (defaultRules.Count == 0) return;

        // 当前已命中的维度集合
        var hitDimensions = hits.Select(h => h.Dimension).Distinct().ToHashSet();
        foreach (var rule in defaultRules)
        {
            if (hitDimensions.Contains(rule.Dimension))
                continue;

            var securePriority = rule.Priority == 0 ? -9999 : rule.Priority;
            // 添加默认分类标签
            hits.Add(new TagHit(
                rule.Dimension,
                rule.DefaultTagName, // 采用你重命名后的属性
                string.Empty,
                -1,
                0,
                securePriority,
                null
            ));
        }
    }
}
