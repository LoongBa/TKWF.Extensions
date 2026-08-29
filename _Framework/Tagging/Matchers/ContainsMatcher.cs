using System;
using System.Collections.Generic;

namespace TKWF.Ext.Tagging.Matchers;

/// <summary>
/// 基础包含匹配器：在全文中查找子串
/// 高性能零分配搜索机制
/// </summary>
public class ContainsMatcher : ITagMatcher
{
    public TagMatchMode Mode => TagMatchMode.Contains;

    public IEnumerable<TagHit> Match(string text, IReadOnlyList<TokenText> tokens, TagRule rule)
    {
        if (string.IsNullOrEmpty(rule.Pattern) || string.IsNullOrEmpty(text))
        {
            return [];
        }

        var results = new List<TagHit>();
        var span = text.AsSpan();
        var patternSpan = rule.Pattern.AsSpan();
        var globalOffset = 0; // 记录在原始字符串中的绝对起始位置

        while (true)
        {
            // 利用底层 SIMD 硬件加速的 Span 搜索
            var index = span.IndexOf(patternSpan, StringComparison.OrdinalIgnoreCase);
            if (index == -1) break;

            var absoluteIndex = globalOffset + index;

            // 仅在命中时才实例化字符串 (记录原词，处理大小写不一致的情况)
            var matchedText = text.Substring(absoluteIndex, patternSpan.Length);

            results.Add(new TagHit(
                rule.Dimension,
                rule.TagName,
                matchedText,
                absoluteIndex,
                patternSpan.Length,
                rule.Priority,
                rule.ExclusionGroup
            ));

            // 滚动游标：跳过当前已匹配的部分，继续向后搜索
            var advance = index + patternSpan.Length;
            span = span.Slice(advance);
            globalOffset += advance;
        }

        return results;
    }
}
