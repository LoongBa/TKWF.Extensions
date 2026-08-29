using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TKWF.Ext.Tagging.Matchers;

/// <summary>
/// 正则匹配器：支持复杂模式提取与高性能双通道
/// 利用 .NET 原生正则缓存与 Span 零分配枚举
/// </summary>
public class RegexMatcher : ITagMatcher
{
    public TagMatchMode Mode => TagMatchMode.Regex;

    // 全局正则缓存：保证同一条规则的正则对象只编译一次
    // 采用 RegexOptions.Compiled 提升运行时执行速度
    private static readonly ConcurrentDictionary<string, Regex> _RegexCache = new();

    public IEnumerable<TagHit> Match(string text, IReadOnlyList<TokenText> tokens, TagRule rule)
    {
        if (string.IsNullOrEmpty(rule.Pattern) || string.IsNullOrEmpty(text))
        {
            return [];
        }

        // 1. 从缓存获取或新建编译后的正则对象
        var regex = _RegexCache.GetOrAdd(rule.Pattern, pattern =>
            new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant));

        var results = new List<TagHit>();

        // 2. 路由预判：检查是否包含复杂的捕获组 (如 $1, $2, ${name})
        var hasDollar = rule.TagName.Contains('$');
        var needsCaptureGroups = false;

        if (hasDollar)
        {
            // 简单高效的检查：是否使用了 $1~$9 或是命名捕获组 ${
            for (var i = 1; i <= 9; i++)
            {
                if (rule.TagName.Contains($"${i}"))
                {
                    needsCaptureGroups = true;
                    break;
                }
            }
            if (!needsCaptureGroups && rule.TagName.Contains("${"))
            {
                needsCaptureGroups = true;
            }
        }

        // 3. 执行匹配路由
        if (needsCaptureGroups)
        {
            // 【慢通道】：需要提取正则括号内的内容，必须分配 Match 对象，使用 .Result() 渲染
            var matches = regex.Matches(text);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                results.Add(new TagHit(
                    rule.Dimension,
                    match.Result(rule.TagName), // 核心：支持 "$1" 等复杂模板的渲染
                    match.Value,
                    match.Index,
                    match.Length,
                    rule.Priority,
                    rule.ExclusionGroup
                ));
            }
        }
        else
        {
            // 【快通道】：静态硬编码标签 或 仅需全文替换($0)
            // 使用 EnumerateMatches 配合 Span 实现零堆内存分配查找
            var span = text.AsSpan();
            foreach (var match in regex.EnumerateMatches(span))
            {
                var matchSpan = span.Slice(match.Index, match.Length);
                var matchString = matchSpan.ToString(); // 高效拿到命中的原词

                // 如果含有 $（此时必然是 $0），则手动替换；否则直接用原 TagName
                var finalTagName = hasDollar
                    ? rule.TagName.Replace("$0", matchString)
                    : rule.TagName;

                results.Add(new TagHit(
                    rule.Dimension,
                    finalTagName,
                    matchString,
                    match.Index,
                    match.Length,
                    rule.Priority,
                    rule.ExclusionGroup
                ));
            }
        }

        return results;
    }
}
