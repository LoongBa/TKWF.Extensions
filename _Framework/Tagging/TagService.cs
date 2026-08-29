#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签服务 (提供给业务层的统一门面)
/// </summary>
public class TagService(TagExtractionPipeline pipeline, string separator = " ") : ITagService
{
    private readonly string _separator = separator;
    private List<TagRule> _Rules = [];

    /// <summary>
    /// 初始化/更新规则
    /// </summary>
    public void LoadRules(IEnumerable<TagRule>? rules)
    {
        var enabledRules = rules?.Where(r => r.IsEnabled).ToList() ?? [];

        // 校验：每个维度最多只有一个 Fallback 规则
        var fallbackValidation = enabledRules
            .Where(r => r.IsDefaultRule)
            .GroupBy(r => r.Dimension)
            .Where(g => g.Count() > 1);

        if (fallbackValidation.Any())
        {
            var invalidDims = string.Join(", ", fallbackValidation.Select(g => g.Key));
            throw new InvalidOperationException(
                $"每个维度只能有一个 Fallback 规则。冲突维度: {invalidDims}");
        }

        _Rules = enabledRules;
    }

    /// <summary>
    /// 版本1：获取标签列表（包含 Fallback 逻辑）
    /// </summary>
    public IReadOnlyList<TagHit> GetTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _Rules.Count == 0)
            return Array.Empty<TagHit>();

        var rawHits = pipeline.Extract(text, _Rules);

        // 物理去重
        var uniqueHits = rawHits
            .DistinctBy(h => new { h.Dimension, h.TagName })
            .ToList();

        return uniqueHits;
    }

    /// <summary>
    /// 版本2：获取格式化后的标签字符串
    /// </summary>
    public string GetTagsString(string text)
    {
        var hits = GetTags(text);
        if (hits.Count == 0)
            return string.Empty;

        var formatted = hits.Select(h =>
            $"{Sanitize(h.Dimension)}:{Sanitize(h.TagName)}");

        return string.Join(_separator, formatted);
    }

    private string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var replacement = _separator == " " ? "_" : "-";
        return input.Replace(_separator, replacement);
    }

    // ====================== 新增便捷方法 ======================

    /// <summary>
    /// 按维度分组返回标签（便于统计和图表）
    /// </summary>
    public Dictionary<string, List<TagHit>> GetTagsByDimension(string text)
    {
        var hits = GetTags(text);
        return hits.GroupBy(h => h.Dimension)
                   .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// 获取指定维度的标签（包含可能的 Fallback）
    /// </summary>
    public List<TagHit> GetTagsForDimension(string text, string dimension)
    {
        return GetTags(text)
            .Where(h => h.Dimension == dimension)
            .ToList();
    }

    /// <summary>
    /// 获取所有已注册的维度
    /// </summary>
    public IReadOnlyList<string> GetDimensions()
    {
        return _Rules.Select(r => r.Dimension).Distinct().ToList();
    }
}
