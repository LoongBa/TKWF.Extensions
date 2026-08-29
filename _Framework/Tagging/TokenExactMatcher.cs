using System;
using System.Collections.Generic;

namespace TKWF.Ext.Tagging;

public class TokenExactMatcher : ITagMatcher
{
    public TagMatchMode Mode => TagMatchMode.TokenExact;

    public IEnumerable<TagHit> Match(string text, IReadOnlyList<TokenText> tokens, TagRule rule)
    {
        var results = new List<TagHit>();

        foreach (var token in tokens)
        {
            // 通过坐标还原 Span，进行零分配比较
            var segment = token.GetSpan(text);

            if (segment.Equals(rule.Pattern.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new TagHit(
                    rule.Dimension,
                    rule.TagName,
                    segment.ToString(), // 命中时再转化为 string
                    token.StartIndex,
                    token.Length,
                    rule.Priority,
                    rule.ExclusionGroup
                ));
            }
        }

        return results;
    }
}
