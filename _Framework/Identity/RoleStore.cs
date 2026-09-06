using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 角色存储实现——经 <see cref="RoleEntityDataService"/> + <see cref="UserRoleEntityDataService"/>
    /// （SG1/xCodeGen 生成的 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class RoleStore : IRoleStore
    {
        private readonly RoleEntityDataService _roleDataService;
        private readonly UserRoleEntityDataService _userRoleDataService;
        private readonly ILogger<RoleStore> _logger;

        public RoleStore(
            RoleEntityDataService roleDataService,
            UserRoleEntityDataService userRoleDataService,
            ILogger<RoleStore> logger)
        {
            _roleDataService = roleDataService ?? throw new ArgumentNullException(nameof(roleDataService));
            _userRoleDataService = userRoleDataService ?? throw new ArgumentNullException(nameof(userRoleDataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<RoleEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            try { return await _roleDataService.GetEntityByIdAsync(id, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色读取失败: Id={Id}", id); return null; }
        }

        public async Task<RoleEntity?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            try { return await _roleDataService.GetByNameAsync(name, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色按名称读取失败: Name={Name}", name); return null; }
        }

        public async Task<IReadOnlyList<RoleEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default)
        {
            try { return await _roleDataService.GetRolesPagedAsync(skip, take, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色列表读取失败"); return Array.Empty<RoleEntity>(); }
        }

        public async Task CreateAsync(RoleEntity role, CancellationToken ct = default)
        {
            if (role == null) return;
            try { await _roleDataService.CreateAsync(role, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色创建失败: Name={Name}", role.Name); }
        }

        public async Task UpdateAsync(RoleEntity role, CancellationToken ct = default)
        {
            if (role == null) return;
            try { await _roleDataService.UpdateAsync(role, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色更新失败: Id={Id}", role.Id); }
        }

        public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
        {
            try
            {
                // 保护逻辑：系统角色 / 已分配用户 → 拒绝删除
                var role = await _roleDataService.GetEntityByIdAsync(id, ct);
                if (role == null) return false;
                if (role.IsSystemRole) return false;
                if (await _userRoleDataService.RoleHasUsersAsync(id, ct)) return false;

                await _roleDataService.DeleteEntityAsync(id, ct);
                return true;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "角色删除失败: Id={Id}", id); return false; }
        }
    }
}