using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 角色存储抽象——角色实体的 CRUD 与查询。
    /// <para>由扩展默认实现（<see cref="RoleStore"/>，继承 <see cref="TKW.Framework.Domain.DomainServiceBase"/>，
    /// <c>AddConstructibleService</c> 注册），消费方可自定义（V0.5.0 起经 AddScoped 覆盖）。</para>
    /// </summary>
    public interface IRoleStore : IDomainService
    {
        /// <summary>按 ID 读取角色（不存在返回 null）。</summary>
        Task<RoleEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>按角色名读取角色（不存在返回 null）。</summary>
        Task<RoleEntity?> GetByNameAsync(string name, CancellationToken ct = default);

        /// <summary>分页读取角色列表。</summary>
        Task<IReadOnlyList<RoleEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default);

        /// <summary>创建角色。</summary>
        Task CreateAsync(RoleEntity role, CancellationToken ct = default);

        /// <summary>更新角色。</summary>
        Task UpdateAsync(RoleEntity role, CancellationToken ct = default);

        /// <summary>
        /// 删除角色。
        /// <para>系统角色（IsSystemRole=true）或已分配用户的角色不可删除，返回 false；成功返回 true。</para>
        /// </summary>
        Task<bool> DeleteAsync(long id, CancellationToken ct = default);
    }
}