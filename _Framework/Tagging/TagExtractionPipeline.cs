using System;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签提取流水线
/// </summary>
public class TagExtractionPipeline
{
    private readonly ITokenizer _Tokenizer;
    private readonly Dictionary<TagMatchMode, ITagMatcher> _Matchers;
    private readonly IEnumerable<ITagPipelinePostProcessor> _PostProcessors;

    // 使用主构造函数注入依赖
    public TagExtractionPipeline(
        ITokenizer tokenizer,
        IEnumerable<ITagMatcher> matchers,
        IEnumerable<ITagPipelinePostProcessor> postProcessors)
    {
        _Tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _Matchers = matchers.ToDictionary(m => m.Mode);
        _PostProcessors = postProcessors ?? [];
    }
    /// <summary>暴露规则给后置处理器（如果需要更灵活的动态规则）</summary>
    public IReadOnlyList<TagRule> Rules { get; private set; } = [];

    /// <summary>
    /// 执行标签提取
    /// </summary>
    public IReadOnlyList<TagHit> Extract(string text, IEnumerable<TagRule> rules)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        // 【优化点】提前过滤并转化为 List，避免后续在 Matcher 循环和 PostProcessor 循环中多次重复枚举
        var enabledRules = rules.Where(r => r.IsEnabled).ToList();
        if (enabledRules.Count == 0) return [];

        var tokens = new List<TokenText>(capacity: 64);
        _Tokenizer.Tokenize(text, tokens.Add);

        var rawHits = new List<TagHit>();

        // 2. 匹配阶段：复用 enabledRules
        foreach (var rule in enabledRules)
        {
            if (_Matchers.TryGetValue(rule.MatchMode, out var matcher))
            {
                var hits = matcher.Match(text, tokens, rule);
                rawHits.AddRange(hits);
            }
        }

        // 3. 后置处理阶段：向所有后置处理器传递当前上下文规则
        foreach (var processor in _PostProcessors)
        {
            processor.Process(rawHits, enabledRules); // ← 修改此处，传入 enabledRules
        }

        return rawHits.AsReadOnly();
    }
}
