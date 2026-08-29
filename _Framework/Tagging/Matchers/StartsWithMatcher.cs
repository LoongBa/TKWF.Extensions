using System;
using System.Collections.Generic;

namespace TKWF.Ext.Tagging.Matchers;

/// <summary>
/// 前缀匹配器：判断文本是否以指定模式开头
/// </summary>
public class StartsWithMatcher : ITagMatcher
{
    public TagMatchMode Mode => TagMatchMode.StartsWith;

    public IEnumerable<TagHit> Match(string text, IReadOnlyList<TokenText> tokens, TagRule rule)
    {
        if (string.IsNullOrEmpty(rule.Pattern) || string.IsNullOrEmpty(text))
        {
            return [];
        }

        var span = text.AsSpan();
        var patternSpan = rule.Pattern.AsSpan();

        // 原地对比，零分配
        if (span.StartsWith(patternSpan, StringComparison.OrdinalIgnoreCase))
        {
            // 命中时，使用 C# 12 集合表达式返回单元素数组
            return [new TagHit(
                rule.Dimension,
                rule.TagName,
                text.Substring(0, patternSpan.Length), // 截取原词以保留大小写原貌
                0, // 起始位置必然是 0
                patternSpan.Length,
                rule.Priority,
                rule.ExclusionGroup
            )];
        }

        return [];
    }
}

/// <summary>
/// 后缀匹配器：判断文本是否以指定模式结尾
/// </summary>
public class EndsWithMatcher : ITagMatcher
{
    public TagMatchMode Mode => TagMatchMode.EndsWith;

    public IEnumerable<TagHit> Match(string text, IReadOnlyList<TokenText> tokens, TagRule rule)
    {
        if (string.IsNullOrEmpty(rule.Pattern) || string.IsNullOrEmpty(text))
        {
            return [];
        }

        var span = text.AsSpan();
        var patternSpan = rule.Pattern.AsSpan();

        if (span.EndsWith(patternSpan, StringComparison.OrdinalIgnoreCase))
        {
            var startIndex = text.Length - patternSpan.Length;

            return [new TagHit(
                rule.Dimension,
                rule.TagName,
                text.Substring(startIndex, patternSpan.Length),
                startIndex,
                patternSpan.Length,
                rule.Priority,
                rule.ExclusionGroup
            )];
        }

        return [];
    }
}
