using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Cryptography;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 用户管理实现——组合 <see cref="IUserStore"/>（用户）+ <see cref="IRoleStore"/>（角色）。
    /// <para>密码用主框架 <see cref="PasswordHasher"/> 散列/校验（PBKDF2，不引第三方库）；异常静默处理。</para>
    /// </summary>
    internal sealed class UserManager : IUserManager
    {
        private readonly IUserStore _userStore;
        private readonly IRoleStore _roleStore;
        private readonly IdentityOptions _options;
        private readonly ILogger<UserManager> _logger;

        public UserManager(IUserStore userStore, IRoleStore roleStore, IOptions<IdentityOptions> options, ILogger<UserManager> logger)
        {
            _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
            _roleStore = roleStore ?? throw new ArgumentNullException(nameof(roleStore));
            _options = options?.Value ?? new IdentityOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserEntity?> GetByIdAsync(long id, CancellationToken ct = default)
            => await _userStore.GetByIdAsync(id, ct);

        public async Task<UserEntity?> FindByNameAsync(string userName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userName)) return null;
            return await _userStore.GetByUserNameAsync(NormalizeUserName(userName), ct);
        }

        public async Task<UserEntity?> CreateUserAsync(string userName, string password, string displayName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userName)) return null;
            if (string.IsNullOrWhiteSpace(password) || password.Length < _options.PasswordMinLength)
            {
                _logger.LogWarning("用户创建失败: 密码长度不足 {Min} 位", _options.PasswordMinLength);
                return null;
            }

            try
            {
                var user = new UserEntity
                {
                    UserName = userName.Trim(),
                    NormalizedUserName = NormalizeUserName(userName),
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName.Trim() : displayName,
                    PasswordHash = PasswordHasher.HashPassword(password),
                    IsActive = true
                };
                await _userStore.CreateAsync(user, ct);
                return user.Id > 0 ? user : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户创建失败: UserName={UserName}", userName);
                return null;
            }
        }

        public async Task UpdateUserAsync(UserEntity user, CancellationToken ct = default)
            => await _userStore.UpdateAsync(user, ct);

        public async Task DeleteUserAsync(long id, CancellationToken ct = default)
            => await _userStore.DeleteAsync(id, ct);

        public async Task ChangePasswordAsync(long userId, string newPassword, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < _options.PasswordMinLength)
            {
                _logger.LogWarning("密码修改失败: 密码长度不足 {Min} 位", _options.PasswordMinLength);
                return;
            }

            var user = await _userStore.GetByIdAsync(userId, ct);
            if (user == null) return;

            try
            {
                user.PasswordHash = PasswordHasher.HashPassword(newPassword);
                user.UpdateTime = DateTimeOffset.Now;
                await _userStore.UpdateAsync(user, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "密码修改失败: UserId={UserId}", userId);
            }
        }

        public async Task<UserEntity?> VerifyCredentialsAsync(string userName, string password, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password)) return null;

            var user = await FindByNameAsync(userName, ct);
            if (user == null) return null;

            // 禁用用户不可登录
            if (!user.IsActive) return null;

            if (string.IsNullOrEmpty(user.PasswordHash)) return null;

            return PasswordHasher.VerifyPassword(password, user.PasswordHash!) ? user : null;
        }

        public async Task<IReadOnlyList<RoleEntity>> GetUserRolesAsync(long userId, CancellationToken ct = default)
            => await _userStore.GetRolesAsync(userId, ct);

        public async Task AssignRolesAsync(long userId, IEnumerable<long> roleIds, CancellationToken ct = default)
        {
            if (roleIds == null) return;

            try
            {
                var desired = roleIds.Distinct().ToList();

                // 先分配目标角色（幂等）——中途失败时旧角色仍保留，避免角色丢失（V0.1.1 原子性修复）
                foreach (var roleId in desired)
                {
                    await _userStore.AssignRoleAsync(userId, roleId, ct);
                }

                // 再清理不在目标集的旧角色
                var existing = await _userStore.GetRolesAsync(userId, ct);
                foreach (var role in existing)
                {
                    if (!desired.Contains(role.Id))
                        await _userStore.RemoveRoleAsync(userId, role.Id, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "角色分配失败: UserId={UserId}", userId);
            }
        }

        public async Task RemoveRoleAsync(long userId, long roleId, CancellationToken ct = default)
            => await _userStore.RemoveRoleAsync(userId, roleId, ct);

        public async Task<RoleEntity?> CreateRoleAsync(string name, string displayName, bool isSystemRole = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            try
            {
                var role = new RoleEntity
                {
                    Name = name.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? name.Trim() : displayName,
                    IsSystemRole = isSystemRole
                };
                await _roleStore.CreateAsync(role, ct);
                return role.Id > 0 ? role : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "角色创建失败: Name={Name}", name);
                return null;
            }
        }

        public async Task<bool> DeleteRoleAsync(long id, CancellationToken ct = default)
            => await _roleStore.DeleteAsync(id, ct);

        /// <summary>用户名规范化（大写，供大小写无关查询）。</summary>
        private static string NormalizeUserName(string userName) => userName.Trim().ToUpperInvariant();
    }
}