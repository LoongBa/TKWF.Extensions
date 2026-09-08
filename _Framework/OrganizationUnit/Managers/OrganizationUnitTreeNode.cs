using System.Collections.Generic;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元树形节点——从 <see cref="OrganizationUnitEntity"/> 递归组装的嵌套树。
    /// <para>消费方通过 <see cref="IOrganizationUnitManager.GetTreeAsync"/> 获取全树（虚拟根，Id=0）。</para>
    /// </summary>
    /// <param name="Id">组织单元 Id（虚拟根为 0）。</param>
    /// <param name="Code">组织单元编码。</param>
    /// <param name="Name">显示名。</param>
    /// <param name="SortOrder">同级排序（小值在前）。</param>
    /// <param name="IsEnabled">是否启用。</param>
    /// <param name="Children">子节点列表（叶子节点为空列表）。</param>
    public sealed record OrganizationUnitTreeNode(
        long Id,
        string Code,
        string Name,
        int SortOrder,
        bool IsEnabled,
        IReadOnlyList<OrganizationUnitTreeNode> Children);
}
