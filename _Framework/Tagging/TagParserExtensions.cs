#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Tagging;

public static class TagParserExtensions
{
    /// <summary>
    /// 将存储在数据库中的 Tag 字符串反序列化为对象列表
    /// </summary>
    public static List<TagHit> ParseTags(this string tagsString, string separator = " ")
    {
        if (string.IsNullOrWhiteSpace(tagsString))
            return [];

        // 1. 按分隔符拆分
        return tagsString.Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                // 2. 按 ':' 拆分维度和标签名
                var segments = part.Split(':');
                return segments.Length != 2 ? null :
                    // 3. 还原可能的清洗字符（如果必要，这里简单返回原始值）
                    new TagHit(segments[0], segments[1], string.Empty, 0, 0, 0, null);
            })
            .Where(x => x != null).ToList()!;
    }
}
