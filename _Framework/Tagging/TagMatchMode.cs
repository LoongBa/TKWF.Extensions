namespace TKWF.Ext.Tagging;

/// <summary>
/// 匹配模式
/// </summary>
public enum TagMatchMode
{
    Contains = 0,
    StartsWith = 1,
    EndsWith = 2,
    Regex = 3,
    FullMatch = 4,
    TokenExact = 5,
    // 高级文本模式
    DictMatch = 10,        // 词典批量匹配（AC自动机）
    Proximity = 11,        // 邻近度匹配
    Logical = 12,          // 逻辑组合匹配
    Fuzzy = 13,            // 模糊纠错匹配

    // 智能语义模式
    Semantic = 20          // 向量语义匹配
}
