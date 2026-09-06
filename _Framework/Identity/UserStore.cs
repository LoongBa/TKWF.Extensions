using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 用户存储实现——经 <see cref="UserEntityDataService"/> + <see cref="UserRoleEntityDataService"/> +
    /// <see cref="RoleEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，遵循数据访问红线
    /// （2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class UserStore : IUserStore
    {
        private readonly UserEntityDataService _userDataService;
        private readonly UserRoleEntityDataService _userRoleDataService;
        private readonly RoleEntityDataService _roleDataService;
        private readonly ILogger<UserStore> _logger;

        public UserStore(
            UserEntityDataService userDataService,
            UserRoleEntityDataService userRoleDataService,
            RoleEntityDataService roleDataService,
            ILogger<UserStore> logger)
        {
            _userDataService = userDataService ?? throw new ArgumentNullException(nameof(userDataService));
            _userRoleDataService = userRoleDataService ?? throw new ArgumentNullException(nameof(userRoleDataService));
            _roleDataService = roleDataService ?? throw new ArgumentNullException(nameof(roleDataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            try { return await _userDataService.GetEntityByIdAsync(id, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户读取失败: Id={Id}", id); return null; }
        }

        public async Task<UserEntity?> GetByUserNameAsync(string normalizedUserName, CancellationToken ct = default)
        {
            try { return await _userDataService.GetByNormalizedUserNameAsync(normalizedUserName, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户按用户名读取失败: UserName={UserName}", normalizedUserName); return null; }
        }

        public async Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default)
        {
            try { return await _userDataService.GetByEmailAsync(email, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户按邮箱读取失败: Email={Email}", email); return null; }
        }

        public async Task<IReadOnlyList<UserEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default)
        {
            try { return await _userDataService.GetUsersPagedAsync(skip, take, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户列表读取失败"); return Array.Empty<UserEntity>(); }
        }

        public async Task CreateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) return;
            try { await _userDataService.CreateAsync(user, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户创建失败: UserName={UserName}", user.UserName); }
        }

        public async Task UpdateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) return;
            try { await _userDataService.UpdateAsync(user, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户更新失败: Id={Id}", user.Id); }
        }

        public async Task DeleteAsync(long id, CancellationToken ct = default)
        {
            try
            {
                // 级联：先删用户-角色分配，再删用户
                await _userRoleDataService.DeleteByUserIdAsync(id, ct);
                await _userDataService.DeleteEntityAsync(id, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "用户删除失败: Id={Id}", id); }
        }

        public async Task<IReadOnlyList<RoleEntity>> GetRolesAsync(long userId, CancellationToken ct = default)
        {
            try
            {
                // 跨表：先查 UserRole 表拿 RoleId 列表，再查 Role 表
                var roleIds = await _userRoleDataService.GetRoleIdsByUserIdAsync(userId, ct);
                if (roleIds.Count == 0) return Array.Empty<RoleEntity>();
                var roles = await _roleDataService.EntitySelectAsync(r => roleIds.Contains(r.Id), 0, 1000, ct: ct);
                return roles;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "用户角色读取失败: UserId={UserId}", userId); return Array.Empty<RoleEntity>(); }
        }

        public async Task AssignRoleAsync(long userId, long roleId, CancellationToken ct = default)
        {
            try
            {
                var exists = await _userRoleDataService.ExistsAsync(userId, roleId, ct);
                if (exists) return; // 幂等
                await _userRoleDataService.CreateAsync(new UserRoleEntity { UserId = userId, RoleId = roleId }, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "用户角色分配失败: UserId={UserId}, RoleId={RoleId}", userId, roleId); }
        }

        public async Task RemoveRoleAsync(long userId, long roleId, CancellationToken ct = default)
        {
            try { await _userRoleDataService.DeleteAsync(userId, roleId, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户角色移除失败: UserId={UserId}, RoleId={RoleId}", userId, roleId); }
        }
    }
}