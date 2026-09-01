using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 用户管理门面——用户 CRUD、凭据验证（供消费方 UserHelper 登录钩子调用）、角色分配、角色 CRUD。
    /// <para>由扩展默认实现（<see cref="UserManager"/>），消费方可自定义（TryAdd 语义）。</para>
    /// </summary>
    public interface IUserManager
    {
        /// <summary>按 ID 读取用户（不存在返回 null）。</summary>
        Task<UserEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>按登录名读取用户（大小写不敏感；不存在返回 null）。</summary>
        Task<UserEntity?> FindByNameAsync(string userName, CancellationToken ct = default);

        /// <summary>创建用户（密码用 <c>PasswordHasher</c> 散列存储）。</summary>
        Task<UserEntity?> CreateUserAsync(string userName, string password, string displayName, CancellationToken ct = default);

        /// <summary>更新用户。</summary>
        Task UpdateUserAsync(UserEntity user, CancellationToken ct = default);

        /// <summary>删除用户（级联清理角色分配）。</summary>
        Task DeleteUserAsync(long id, CancellationToken ct = default);

        /// <summary>修改用户密码（重新散列存储）。</summary>
        Task ChangePasswordAsync(long userId, string newPassword, CancellationToken ct = default);

        /// <summary>
        /// 凭据验证——供消费方 <c>DomainUserHelperBase.OnLoginByPasswordAsync</c> 调用。
        /// <para>成功返回用户实体；用户不存在 / 密码错误 / 禁用返回 null。</para>
        /// </summary>
        Task<UserEntity?> VerifyCredentialsAsync(string userName, string password, CancellationToken ct = default);

        /// <summary>读取用户的所有角色（未分配返回空列表）。</summary>
        Task<IReadOnlyList<RoleEntity>> GetUserRolesAsync(long userId, CancellationToken ct = default);

        /// <summary>为用户全量分配角色（先清空已分配，再逐个分配）。</summary>
        Task AssignRolesAsync(long userId, IEnumerable<long> roleIds, CancellationToken ct = default);

        /// <summary>移除用户的一个角色。</summary>
        Task RemoveRoleAsync(long userId, long roleId, CancellationToken ct = default);

        /// <summary>创建角色。</summary>
        Task<RoleEntity?> CreateRoleAsync(string name, string displayName, bool isSystemRole = false, CancellationToken ct = default);

        /// <summary>删除角色（系统角色/已分配角色不可删除，返回 false）。</summary>
        Task<bool> DeleteRoleAsync(long id, CancellationToken ct = default);
    }
}