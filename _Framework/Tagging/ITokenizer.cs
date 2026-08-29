using System;
using System.Collections.Generic;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 分词器接口
/// </summary>
public interface ITokenizer
{
    /// <summary>
    /// 将文本进行分词。
    /// </summary>
    void Tokenize(string text, Action<TokenText> receiver);
}

/// <summary>
/// 匹配器策略接口
/// </summary>
public interface ITagMatcher
{
    TagMatchMode Mode { get; }
    IEnumerable<TagHit> Match(string text, IReadOnlyList<TokenText> tokens, TagRule rule);
}

/// <summary>
/// 管道后置处理器接口 (用于去重、互斥处理等)
/// </summary>
public interface ITagPipelinePostProcessor
{
    void Process(List<TagHit> hits, IReadOnlyList<TagRule> contextRules);
}
