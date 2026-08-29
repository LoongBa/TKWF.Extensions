using System.Collections.Generic;

namespace TKWF.Ext.Tagging;

/// <summary>标签服务接口（V4.9.79 D17 模式 3：接口 + 实现全在扩展包内）。</summary>
public interface ITagService
{
    void LoadRules(IEnumerable<TagRule>? rules);
    IReadOnlyList<TagHit> GetTags(string text);
    string GetTagsString(string text);
    Dictionary<string, List<TagHit>> GetTagsByDimension(string text);
    List<TagHit> GetTagsForDimension(string text, string dimension);
    IReadOnlyList<string> GetDimensions();
}
