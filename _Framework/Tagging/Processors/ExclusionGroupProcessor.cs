using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Tagging.Processors;

/// <summary>
/// 互斥组处理器：处理同一互斥组内的标签冲突
/// 规则：同一个 ExclusionGroup 内，仅保留 Priority 最高的标签命中记录
/// </summary>
public class ExclusionGroupProcessor : ITagPipelinePostProcessor
{
    public void Process(List<TagHit> hits, IReadOnlyList<TagRule> contextRules)
    {
        // 如果命中结果少于2个，不可能发生互斥，直接短路返回
        if (hits.Count <= 1) return;

        // 1. 筛选出具有互斥组属性的记录，并按互斥组进行分组
        var groupedHits = hits
            .Where(h => !string.IsNullOrEmpty(h.ExclusionGroup))
            .GroupBy(h => h.ExclusionGroup!)
            .ToList();

        // 如果没有任何标签定义了互斥组，直接返回
        if (groupedHits.Count == 0) return;

        // 使用 HashSet 记录需要剔除的冗余项，保证查找效率为 O(1)
        var hitsToRemove = new HashSet<TagHit>();

        foreach (var group in groupedHits)
        {
            // 在当前互斥组内，按优先级降序排列，并获取最高优先级的那一个
            // 注意：如果有多个同等最高优先级的标签，这里默认保留匹配到的第一个
            var highestPriorityHit = group.OrderByDescending(h => h.Priority).First();

            // 遍历组内元素，将非最高优先级的元素加入待移除集合
            foreach (var hit in group)
            {
                // 注意：TagHit 是 record，这里使用的是值级比对 (Value Equality)
                // 如果需要严格基于引用比对，可以使用 !ReferenceEquals(hit, highestPriorityHit)
                if (hit != highestPriorityHit)
                {
                    hitsToRemove.Add(hit);
                }
            }
        }

        // 2. 批量剔除冲突记录（RemoveAll 底层是双指针覆盖算法，只需遍历一次，性能极高）
        if (hitsToRemove.Count > 0)
        {
            hits.RemoveAll(hitsToRemove.Contains);
        }
    }
}
