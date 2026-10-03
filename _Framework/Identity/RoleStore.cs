using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 角色存储实现——经 <see cref="RoleEntityDataService"/> + <see cref="UserRoleEntityDataService"/>
    /// （SG1/xCodeGen 生成的 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）：继承 <see cref="DomainServiceBase"/>——经基类
    /// <c>User</c> 获取用户上下文（IDomainUser 永不注册 DI——旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）；
    /// DataService 仍经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载（DI004 零豁免）。
    /// <c>[DiContractIgnore]</c>：运行时手写注册（AddConstructibleService），豁免 SG1a DI001 误报。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class RoleStore : DomainServiceBase, IRoleStore
    {
        private readonly ILogger<RoleStore> _logger;

        private RoleEntityDataService? _roleDataService;
        private UserRoleEntityDataService? _userRoleDataService;

        private RoleEntityDataService RoleDataService => _roleDataService ??= User.Use<RoleEntityDataService>();
        private UserRoleEntityDataService UserRoleDataService => _userRoleDataService ??= User.Use<UserRoleEntityDataService>();

        public RoleStore(IDomainUser user, ILogger<RoleStore> logger) : base(user)
        {
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
