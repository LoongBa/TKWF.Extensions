using System.Collections.Generic;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典树形节点（V0.2.0）——从 <see cref="DictionaryItemEntity"/> 递归组装的树结构。
    /// <para>消费方通过 <see cref="IDictionaryManager.GetItemsTreeAsync"/> 获取嵌套树。</para>
    /// </summary>
    /// <param name="Code">字典项编码。</param>
    /// <param name="DisplayName">显示名。</param>
    /// <param name="Value">关联值（可为空）。</param>
    /// <param name="Order">排序（小值在前）。</param>
    /// <param name="IsEnabled">是否启用。</param>
    /// <param name="Children">子节点列表（叶子节点为空列表）。</param>
    public record DictionaryTreeNode(
        string Code,
        string DisplayName,
        string? Value,
        int Order,
        bool IsEnabled,
        IReadOnlyList<DictionaryTreeNode> Children);
}
