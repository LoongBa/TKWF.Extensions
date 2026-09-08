using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元管理门面（公开）——树形 OU 增删改/移动（循环防护 + 子树路径重算）+ 用户关联 + 双向查询。
    /// <para>业务规则：Code 白名单（C3）、Path 长度守卫（C3）、删除保护（D5）、移动循环防护（D4）、
    /// 物理删除语义（C2）、Create/Move/Delete 写路径事务包裹（C1）。</para>
    /// </summary>
    public interface IOrganizationUnitManager
    {
        /// <summary>创建组织单元（根 ParentId=null → Level=0, Path="/Code/"；子节点 → 父+1 且 Path 继承父前缀）。</summary>
        Task<OrganizationUnitEntity> CreateAsync(string code, string name, long? parentId, int? sortOrder = null, CancellationToken ct = default);

        /// <summary>更新组织单元（Code 不可改；仅更新非空字段 + UpdateTime）。</summary>
        Task UpdateAsync(long id, string? name = null, int? sortOrder = null, bool? isEnabled = null, CancellationToken ct = default);

        /// <summary>删除组织单元（事务内二次确认无子节点 + 无关联用户 → 清 junction → 物理删）。</summary>
        Task DeleteAsync(long id, CancellationToken ct = default);

        /// <summary>移动组织单元到新父节点（循环防护 + BFS 子树 Level/Path 重算 + 事务原子提交）。</summary>
        Task MoveAsync(long id, long? newParentId, CancellationToken ct = default);

        /// <summary>全量树（内存组装，同级 SortOrder 排序；孤儿节点抛 InvalidOperationException）。</summary>
        Task<OrganizationUnitTreeNode> GetTreeAsync(CancellationToken ct = default);

        /// <summary>子树（含自身，Path 前缀匹配）。</summary>
        Task<IReadOnlyList<OrganizationUnitEntity>> GetSubTreeAsync(long id, CancellationToken ct = default);

        /// <summary>祖先链（从根到直接父，不含自身）。</summary>
        Task<IReadOnlyList<OrganizationUnitEntity>> GetAncestorsAsync(long id, CancellationToken ct = default);

        /// <summary>分配用户到 OU（数据库唯一约束冲突转 InvalidOperationException）。</summary>
        Task AssignUserAsync(long ouId, string userId, CancellationToken ct = default);

        /// <summary>解除 OU 的用户（幂等，不存在静默成功）。</summary>
        Task UnassignUserAsync(long ouId, string userId, CancellationToken ct = default);

        /// <summary>获取 OU（含子树）的用户 Id 列表（去重；两步查询：OU 子树 Id → junction）。</summary>
        Task<IReadOnlyList<string>> GetUserIdsInOrganizationUnitAsync(long ouId, bool includeDescendants, CancellationToken ct = default);

        /// <summary>获取用户所属 OU Id 列表（junction 按 UserId 直查）。</summary>
        Task<IReadOnlyList<long>> GetOrganizationUnitIdsForUserAsync(string userId, CancellationToken ct = default);
    }
}
