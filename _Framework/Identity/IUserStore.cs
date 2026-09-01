using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 用户存储抽象——用户实体的 CRUD 与查询、以及用户-角色分配。
    /// <para>由扩展默认 FreeSql 实现（<see cref="FreeSqlUserStore"/>），消费方可自定义（TryAdd 语义）。</para>
    /// </summary>
    public interface IUserStore
    {
        /// <summary>按 ID 读取用户（不存在返回 null）。</summary>
        Task<UserEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>按规范化用户名读取用户（不存在返回 null）。</summary>
        Task<UserEntity?> GetByUserNameAsync(string normalizedUserName, CancellationToken ct = default);

        /// <summary>按邮箱读取用户（不存在返回 null）。</summary>
        Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default);

        /// <summary>分页读取用户列表。</summary>
        Task<IReadOnlyList<UserEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default);

        /// <summary>创建用户。</summary>
        Task CreateAsync(UserEntity user, CancellationToken ct = default);

        /// <summary>更新用户。</summary>
        Task UpdateAsync(UserEntity user, CancellationToken ct = default);

        /// <summary>删除用户。</summary>
        Task DeleteAsync(long id, CancellationToken ct = default);

        /// <summary>读取用户的所有角色（未分配返回空列表）。</summary>
        Task<IReadOnlyList<RoleEntity>> GetRolesAsync(long userId, CancellationToken ct = default);

        /// <summary>为用户分配角色（幂等，重复分配忽略）。</summary>
        Task AssignRoleAsync(long userId, long roleId, CancellationToken ct = default);

        /// <summary>移除用户的一个角色。</summary>
        Task RemoveRoleAsync(long userId, long roleId, CancellationToken ct = default);
    }
}