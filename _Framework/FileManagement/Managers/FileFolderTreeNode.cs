using System.Collections.Generic;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 目录树形节点——从 <see cref="FileFolderEntity"/> 递归组装的嵌套树。
    /// <para>消费方通过 <see cref="IFileManager.GetFolderTreeAsync"/> 获取全树（虚拟根，Id=0）。</para>
    /// </summary>
    /// <param name="Id">目录 Id（虚拟根为 0）。</param>
    /// <param name="Code">目录编码（不可变，构建 Path 段）。</param>
    /// <param name="Name">显示名。</param>
    /// <param name="SortOrder">同级排序（小值在前）。</param>
    /// <param name="Children">子节点列表（叶子节点为空列表）。</param>
    public sealed record FileFolderTreeNode(
        long Id,
        string Code,
        string Name,
        int SortOrder,
        IReadOnlyList<FileFolderTreeNode> Children);
}
