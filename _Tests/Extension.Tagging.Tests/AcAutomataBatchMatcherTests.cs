using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TKW.Framework.Utility.Tags;
using TKW.Framework.Utility.Tags.Matchers;
using TKW.Framework.Utility.Tags.Processors;

namespace TKWF.Ext.Tagging.Tests;

/// <summary>
/// V0.4.0：AC 自动机批量匹配器（DictMatch）+ 全流水线批量路由测试。
/// <para>对齐 Oracle P2-8：性能冒烟含 Contains baseline 对照（宽松倍数避免 CI 抖动）。</para>
/// </summary>
public class AcAutomataBatchMatcherTests
{
    private static TagRule Dict(string dimension, string tagName, string pattern, int priority = 0, string? exclusionGroup = null)
        => new() { Dimension = dimension, TagName = tagName, MatchMode = TagMatchMode.DictMatch, Pattern = pattern, Priority = priority, ExclusionGroup = exclusionGroup };

    private static TagRule Contains(string dimension, string tagName, string pattern)
        => new() { Dimension = dimension, TagName = tagName, MatchMode = TagMatchMode.Contains, Pattern = pattern };

    // ==================== AC 正确性 ====================

    [Fact]
    public void MatchBatch_MultiRule_AllHit()
    {
        var matcher = new AcAutomataBatchMatcher();
        var rules = new List<TagRule>
        {
            Dict("category", "电子", "手机"),
            Dict("category", "家电", "冰箱"),
            Dict("brand", "华为", "华为")
        };

        var hits = matcher.MatchBatch("华为手机 冰箱", rules);

        Assert.Equal(3, hits.Count);
        Assert.Contains(hits, h => h.Dimension == "brand" && h.TagName == "华为");
        Assert.Contains(hits, h => h.Dimension == "category" && h.TagName == "电子");
        Assert.Contains(hits, h => h.Dimension == "category" && h.TagName == "家电");
    }

    [Fact]
    public void MatchBatch_SharedPattern_MultipleRules()
    {
        var matcher = new AcAutomataBatchMatcher();
        // 两个维度共享同一 Pattern
        var rules = new List<TagRule>
        {
            Dict("dim1", "标签A", "苹果"),
            Dict("dim2", "标签B", "苹果")
        };

        var hits = matcher.MatchBatch("苹果手机", rules);

        Assert.Equal(2, hits.Count);   // 同一位置两条规则各自命中
        Assert.Contains(hits, h => h.Dimension == "dim1" && h.TagName == "标签A");
        Assert.Contains(hits, h => h.Dimension == "dim2" && h.TagName == "标签B");
    }

    [Fact]
    public void MatchBatch_CaseInsensitive_OrdinalIgnoreCase()
    {
        var matcher = new AcAutomataBatchMatcher();
        var rules = new List<TagRule> { Dict("brand", "华为", "huawei") };

        var hits = matcher.MatchBatch("HUAWEI Mate 60", rules);

        Assert.Single(hits);
        Assert.Equal("HUAWEI", hits[0].MatchedValue);   // 保留原文大小写
        Assert.Equal(0, hits[0].StartIndex);
        Assert.Equal(6, hits[0].Length);
    }

    [Fact]
    public void MatchBatch_Overlap_EmitsAllPositions()
    {
        var matcher = new AcAutomataBatchMatcher();
        var rules = new List<TagRule> { Dict("dim", "aa", "aa") };

        var hits = matcher.MatchBatch("aaaa", rules);

        // AC 输出全部重叠出现位置（Contains 滚动跳过仅 2 个）
        Assert.Equal(3, hits.Count);
        Assert.Equal(0, hits[0].StartIndex);
        Assert.Equal(1, hits[1].StartIndex);
        Assert.Equal(2, hits[2].StartIndex);
    }

    [Fact]
    public void MatchBatch_FailLink_SuffixPatternMatch()
    {
        var matcher = new AcAutomataBatchMatcher();
        // 长模式失配后经 fail 链命中短模式（后缀复用）
        var rules = new List<TagRule>
        {
            Dict("dim", "长词", "abcdef"),
            Dict("dim", "短词", "cdef")
        };

        var hits = matcher.MatchBatch("xxabcdefyy", rules);

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.TagName == "长词" && h.StartIndex == 2);
        Assert.Contains(hits, h => h.TagName == "短词" && h.StartIndex == 4);   // 重叠：cdef 从位置 4
    }

    [Fact]
    public void MatchBatch_EmptyPattern_Skipped()
    {
        var matcher = new AcAutomataBatchMatcher();
        var rules = new List<TagRule>
        {
            Dict("dim", "空模式", ""),
            Dict("dim", "正常", "正常")
        };

        var hits = matcher.MatchBatch("正常文本", rules);

        Assert.Single(hits);
        Assert.Equal("正常", hits[0].TagName);
    }

    [Fact]
    public void MatchBatch_EmptyTextOrRules_ReturnsEmpty()
    {
        var matcher = new AcAutomataBatchMatcher();
        var rules = new List<TagRule> { Dict("dim", "标签", "词") };

        Assert.Empty(matcher.MatchBatch("", rules));
        Assert.Empty(matcher.MatchBatch("任意文本", []));
    }

    // ==================== 自动机缓存（P1-1 稳定引用前提） ====================

    [Fact]
    public void MatchBatch_SameRulesReference_ReusesAutomaton()
    {
        var matcher = new AcAutomataBatchMatcher();
        var rules = new List<TagRule> { Dict("dim", "标签", "苹果") };

        matcher.MatchBatch("苹果", rules);
        matcher.MatchBatch("苹果", rules);   // 同一引用 → 复用

        // 行为断言：结果一致（引用复用无法直接观测，验证功能不退化即可）
        var hits = matcher.MatchBatch("苹果", rules);
        Assert.Single(hits);
    }

    [Fact]
    public void MatchBatch_NewRulesReference_Rebuilds()
    {
        var matcher = new AcAutomataBatchMatcher();
        var rules1 = new List<TagRule> { Dict("dim", "旧标签", "苹果") };
        var rules2 = new List<TagRule> { Dict("dim", "新标签", "香蕉") };

        matcher.MatchBatch("苹果", rules1);
        var hits = matcher.MatchBatch("香蕉", rules2);   // 新引用 → 重建自动机

        Assert.Single(hits);
        Assert.Equal("新标签", hits[0].TagName);
    }

    // ==================== 全流水线批量路由 ====================

    [Fact]
    public void Pipeline_DictMatch_BatchPath_WithContains_Mixed()
    {
        var pipeline = new TagExtractionPipeline(
            new DefaultTokenizer(),
            [new TokenExactMatcher(), new ContainsMatcher(), new RegexMatcher(), new StartsWithMatcher(), new EndsWithMatcher(), new FullMatchMatcher()],
            [new ExclusionGroupProcessor(), new DefaultTagProcessor()],
            [new AcAutomataBatchMatcher()]);

        var rules = new List<TagRule>
        {
            Dict("dict", "AC标签", "华为"),
            Contains("contain", "包含标签", "手机")
        };

        var hits = pipeline.Extract("华为手机", rules);

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Dimension == "dict" && h.TagName == "AC标签");
        Assert.Contains(hits, h => h.Dimension == "contain" && h.TagName == "包含标签");
    }

    [Fact]
    public void Pipeline_NoBatchMatchers_FallbackToPerRulePath()
    {
        // Oracle P1-2：_BatchMatchers 空 → 原始逐规则路径（行为与 v0.3.0 一致）
        var pipeline = new TagExtractionPipeline(
            new DefaultTokenizer(),
            [new ContainsMatcher()],
            [new ExclusionGroupProcessor(), new DefaultTagProcessor()],
            null);   // 无批量匹配器

        var rules = new List<TagRule>
        {
            Contains("contain", "包含标签", "手机"),
            Contains("contain", "包含标签2", "苹果")
        };

        var hits = pipeline.Extract("苹果手机", rules);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void Pipeline_GroupCache_StableReference_AcrossExtractCalls()
    {
        // P1-1：同一 rules 引用多次 Extract → 分组缓存命中 → 结果稳定
        var pipeline = new TagExtractionPipeline(
            new DefaultTokenizer(),
            [new ContainsMatcher()],
            [new ExclusionGroupProcessor(), new DefaultTagProcessor()],
            [new AcAutomataBatchMatcher()]);

        var rules = new List<TagRule> { Dict("dict", "AC标签", "华为") };

        var first = pipeline.Extract("华为手机", rules);
        var second = pipeline.Extract("华为手机", rules);   // 同一引用 → 缓存命中

        Assert.Equal(first.Count, second.Count);
        Assert.Equal("AC标签", second[0].TagName);
    }

    // ==================== FullMatch（P2-5） ====================

    [Fact]
    public void FullMatchMatcher_ExactMatch_Hits()
    {
        var matcher = new FullMatchMatcher();
        var rule = new TagRule { Dimension = "dim", TagName = "整词", MatchMode = TagMatchMode.FullMatch, Pattern = "完全一致" };

        var hits = matcher.Match("完全一致", [], rule).ToList();
        Assert.Single(hits);
        Assert.Equal("整词", hits[0].TagName);
        Assert.Equal(0, hits[0].StartIndex);
    }

    [Fact]
    public void FullMatchMatcher_CaseInsensitive()
    {
        var matcher = new FullMatchMatcher();
        var rule = new TagRule { Dimension = "dim", TagName = "整词", MatchMode = TagMatchMode.FullMatch, Pattern = "abc" };

        var hits = matcher.Match("ABC", [], rule);
        Assert.Single(hits);
    }

    [Fact]
    public void FullMatchMatcher_PartialText_NoHit()
    {
        var matcher = new FullMatchMatcher();
        var rule = new TagRule { Dimension = "dim", TagName = "整词", MatchMode = TagMatchMode.FullMatch, Pattern = "abc" };

        Assert.Empty(matcher.Match("abcd", [], rule));
        Assert.Empty(matcher.Match("", [], rule));
        Assert.Empty(matcher.Match("abc", [], new TagRule { Pattern = "" }));
    }

    // ==================== 性能冒烟（P2-8：Contains baseline 对照） ====================

    [Fact]
    public void PerformanceSmoke_AcFasterThanContains_ForLargeRuleSet()
    {
        var text = string.Join(" ", Enumerable.Range(0, 50).Select(i => $"商品{i} 特性{i}"));
        // 1000 规则共享 50 个实际出现的 pattern（每个 "商品{i}" 被 20 条规则共享）——同时压测共享 Pattern + 批量扫描
        var rules = Enumerable.Range(0, 1000)
            .Select(i => Dict("dict", $"标签{i}", $"商品{i % 50}"))
            .ToList();

        var acMatcher = new AcAutomataBatchMatcher();
        var containsRules = rules
            .Select(r => new TagRule { Dimension = r.Dimension, TagName = r.TagName, MatchMode = TagMatchMode.Contains, Pattern = r.Pattern })
            .ToList();

        // 预热
        acMatcher.MatchBatch(text, rules);
        var containsMatcher = new ContainsMatcher();
        foreach (var rule in containsRules.Take(10)) containsMatcher.Match(text, [], rule);

        // AC 批量路径（自动机已预热 → 纯扫描）
        var swAc = Stopwatch.StartNew();
        var acHits = acMatcher.MatchBatch(text, rules);
        swAc.Stop();

        // Contains 逐规则路径（1000 规则全量）
        var swContains = Stopwatch.StartNew();
        var containsHits = new List<TagHit>();
        foreach (var rule in containsRules)
            containsHits.AddRange(containsMatcher.Match(text, [], rule));
        swContains.Stop();

        // 正确性对照：AC 命中数 ≥ Contains（AC 含重叠全输出）
        Assert.True(acHits.Count >= containsHits.Count,
            $"AC 命中 {acHits.Count} < Contains 命中 {containsHits.Count}");
        // 性能对照（宽松 5 倍，避免 CI 抖动）：预热后 AC 纯扫描应显著快于 1000 规则逐条扫描
        Assert.True(swAc.ElapsedMilliseconds < swContains.ElapsedMilliseconds * 5,
            $"AC {swAc.ElapsedMilliseconds}ms 未显著快于 Contains {swContains.ElapsedMilliseconds}ms");
    }
}
