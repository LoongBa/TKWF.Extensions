using System.Collections.Generic;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限定义仓库——运行时收集后的权限定义查询。
    /// <para>由 <see cref="PermissionExtensionInitializer{TUserInfo}"/> 在 ConfigureServices 阶段
    /// 实例化全部贡献者、调用 <c>Define(context)</c> 后填充；供 <see cref="IPermissionChecker"/>
    /// 校验权限名是否存在（fail-closed：未知权限名 → 拒绝）。</para>
    /// </summary>
    public interface IPermissionDefinitionRepository
    {
        /// <summary>全部权限定义（只读快照）。</summary>
        IReadOnlyList<PermissionDefinition> GetAll();

        /// <summary>是否包含指定权限名。</summary>
        bool Contains(string name);

        /// <summary>批量填充权限定义（由初始化器调用，重复名忽略并记录第一条）。</summary>
        void AddRange(IEnumerable<PermissionDefinition> definitions);
    }
}
