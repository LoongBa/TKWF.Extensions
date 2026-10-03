using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 角色存储实现——经 <see cref="RoleEntityDataService"/> + <see cref="UserRoleEntityDataService"/>
    /// （SG1/xCodeGen 生成的 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// <para>ADR88/DI004（A 批整改）：DataService 不再构造注入——经 <see cref="IDomainUser.Use{TDomainService}()"/> 懒加载解析。</para>
    /// </summary>
    internal sealed class RoleStore : IRoleStore
    {
        private readonly IDomainUser _user;
        private readonly ILogger<RoleStore> _logger;

        private RoleEntityDataService? _roleDataService;
        private UserRoleEntityDataService? _userRoleDataService;

        private RoleEntityDataService RoleDataService => _roleDataService ??= _user.Use<RoleEntityDataService>();
        private UserRoleEntityDataService UserRoleDataService => _userRoleDataService ??= _user.Use<UserRoleEntityDataService>();

        public RoleStore(IDomainUser user, ILogger<RoleStore> logger)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<RoleEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            try { return await RoleDataService.GetEntityByIdAsync(id, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色读取失败: Id={Id}", id); return null; }
        }

        public async Task<RoleEntity?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            try { return await RoleDataService.GetByNameAsync(name, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色按名称读取失败: Name={Name}", name); return null; }
        }

        public async Task<IReadOnlyList<RoleEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default)
        {
            try { return await RoleDataService.GetRolesPagedAsync(skip, take, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色列表读取失败"); return Array.Empty<RoleEntity>(); }
        }

        public async Task CreateAsync(RoleEntity role, CancellationToken ct = default)
        {
            if (role == null) return;
            try { await RoleDataService.CreateAsync(role, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色创建失败: Name={Name}", role.Name); }
        }

        public async Task UpdateAsync(RoleEntity role, CancellationToken ct = default)
        {
            if (role == null) return;
            try { await RoleDataService.UpdateAsync(role, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "角色更新失败: Id={Id}", role.Id); }
        }

        public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
        {
            try
            {
                // 保护逻辑：系统角色 / 已分配用户 → 拒绝删除
                var role = await RoleDataService.GetEntityByIdAsync(id, ct);
                if (role == null) return false;
                if (role.IsSystemRole) return false;
                if (await UserRoleDataService.RoleHasUsersAsync(id, ct)) return false;

                await RoleDataService.DeleteEntityAsync(id, ct);
                return true;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "角色删除失败: Id={Id}", id); return false; }
        }
    }
}