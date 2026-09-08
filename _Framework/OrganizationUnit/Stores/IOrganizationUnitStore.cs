using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元存储抽象（internal）——OU 与用户关联的持久化操作，经 SG1 DataService 委托实现。
    /// <para>异常自然传播（不静默）：唯一约束冲突由 <see cref="IOrganizationUnitManager.AssignUserAsync"/>
    /// 捕获转业务异常；事务由 Manager 层统一管理（Store 不触碰 <c>ITransactionManager</c>）。</para>
    /// </summary>
    internal interface IOrganizationUnitStore
    {
        /// <summary>按 Id 读取（不存在返回 null）。</summary>
        Task<OrganizationUnitEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>按 Code 读取（不存在返回 null）。</summary>
        Task<OrganizationUnitEntity?> GetByCodeAsync(string code, CancellationToken ct = default);

        /// <summary>全量读取（按 Level/SortOrder/Id 排序）。</summary>
        Task<IReadOnlyList<OrganizationUnitEntity>> GetAllAsync(CancellationToken ct = default);

        /// <summary>新增组织单元（回写自增 Id，返回 Id）。</summary>
        Task<long> CreateAsync(OrganizationUnitEntity entity, CancellationToken ct = default);

        /// <summary>更新组织单元（全字段更新）。</summary>
        Task UpdateAsync(OrganizationUnitEntity entity, CancellationToken ct = default);

        /// <summary>物理删除组织单元（调用方确保无子节点/无关联用户）。</summary>
        Task DeleteAsync(long id, CancellationToken ct = default);

        /// <summary>新增用户关联（唯一约束冲突异常自然传播）。</summary>
        Task AddUserAsync(OrganizationUnitUserEntity entity, CancellationToken ct = default);

        /// <summary>移除用户关联（幂等，不存在静默成功）。</summary>
        Task RemoveUserAsync(long ouId, string userId, CancellationToken ct = default);

        /// <summary>按 OU Id 集合查关联用户 Id（去重）。</summary>
        Task<IReadOnlyList<string>> GetUserIdsByOrganizationUnitIdsAsync(IReadOnlyList<long> ouIds, CancellationToken ct = default);

        /// <summary>按用户 Id 查其所属 OU Id 列表（去重）。</summary>
        Task<IReadOnlyList<long>> GetOrganizationUnitIdsForUserAsync(string userId, CancellationToken ct = default);

        /// <summary>统计某 OU 关联用户数。</summary>
        Task<long> CountUsersByOrganizationUnitIdAsync(long ouId, CancellationToken ct = default);

        /// <summary>批量删除 OU 集合的全部关联行（删除 OU 前置清空 junction）。</summary>
        Task DeleteUsersByOrganizationUnitIdsAsync(IReadOnlyList<long> ouIds, CancellationToken ct = default);
    }
}
