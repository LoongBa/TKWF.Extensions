using System;
using System.Collections.Generic;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签规则定义
/// </summary>
public class TagRule
{
    public string Dimension { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public TagMatchMode MatchMode { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public int Priority { get; set; } = 0;
    public string? ExclusionGroup { get; set; }
    public bool IsDefaultRule { get; set; } = false;
    public string DefaultTagName { get; set; } = "其它";
}

/// <summary>
/// 标签命中结果
/// </summary>
public record TagHit(
    string Dimension,
    string TagName,
    string MatchedValue,
    int StartIndex,
    int Length,
    int Priority,
    string? ExclusionGroup);

/// <summary>
/// 极致性能的分词单元
/// </summary>
public readonly struct TokenText(int startIndex, int length)
{
    public int StartIndex { get; } = startIndex;
    public int Length { get; } = length;

    public ReadOnlySpan<char> GetSpan(string source) => source.AsSpan(StartIndex, Length);
}
