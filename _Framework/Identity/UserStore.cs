using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 用户存储实现——经 <see cref="UserEntityDataService"/> + <see cref="UserRoleEntityDataService"/> +
    /// <see cref="RoleEntityDataService"/> + <see cref="UserRoleViewDataService"/>（SG1/xCodeGen 生成的 DataService
    /// + VEntity 手写只读 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：
    /// 扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>V0.2.0：GetRolesAsync 改用 <see cref="UserRoleViewDataService"/>（VEntity JOIN 单查询，替代两步查询）。</para>
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）：继承 <see cref="DomainServiceBase"/>——经基类
    /// <c>User</c> 获取用户上下文（IDomainUser 永不注册 DI——旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）；
    /// DataService 仍经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载（DI004 零豁免）。
    /// <c>[DiContractIgnore]</c>：运行时手写注册（AddConstructibleService），豁免 SG1a DI001 误报。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class UserStore : DomainServiceBase, IUserStore
    {
        private readonly ILogger<UserStore> _logger;

        private UserEntityDataService? _userDataService;
        private UserRoleEntityDataService? _userRoleDataService;
        private UserRoleViewDataService? _userRoleViewDataService;

        private UserEntityDataService UserDataService => _userDataService ??= User.Use<UserEntityDataService>();
        private UserRoleEntityDataService UserRoleDataService => _userRoleDataService ??= User.Use<UserRoleEntityDataService>();
        private UserRoleViewDataService UserRoleViewDataService => _userRoleViewDataService ??= User.Use<UserRoleViewDataService>();

        public UserStore(IDomainUser user, ILogger<UserStore> logger) : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            try { return await UserDataService.GetEntityByIdAsync(id, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户读取失败: Id={Id}", id); return null; }
        }

        public async Task<UserEntity?> GetByUserNameAsync(string normalizedUserName, CancellationToken ct = default)
        {
            try { return await UserDataService.GetByNormalizedUserNameAsync(normalizedUserName, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户按用户名读取失败: UserName={UserName}", normalizedUserName); return null; }
        }

        public async Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default)
        {
            try { return await UserDataService.GetByEmailAsync(email, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户按邮箱读取失败: Email={Email}", email); return null; }
        }

        public async Task<IReadOnlyList<UserEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default)
        {
            try { return await UserDataService.GetUsersPagedAsync(skip, take, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户列表读取失败"); return Array.Empty<UserEntity>(); }
        }

        public async Task CreateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) return;
            try { await UserDataService.CreateAsync(user, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户创建失败: UserName={UserName}", user.UserName); }
        }

        public async Task UpdateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) return;
            try { await UserDataService.UpdateAsync(user, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户更新失败: Id={Id}", user.Id); }
        }

        public async Task DeleteAsync(long id, CancellationToken ct = default)
        {
            try
            {
                // 级联：先删用户-角色分配，再删用户
                await UserRoleDataService.DeleteByUserIdAsync(id, ct);
                await UserDataService.DeleteEntityAsync(id, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "用户删除失败: Id={Id}", id); }
        }

        public async Task<IReadOnlyList<RoleEntity>> GetRolesAsync(long userId, CancellationToken ct = default)
        {
            try
            {
                // V0.2.0 VEntity：跨表 JOIN（IdentityUserRole → IdentityRole）单查询下推 DB，替代两步查询
                // （先查 RoleId 集合 → 再按集合查 Role）。视图行映射回 RoleEntity，接口签名不变。
                var views = await UserRoleViewDataService.GetRolesByUserIdAsync(userId, ct);
                return views.Select(v => new RoleEntity
                {
                    Id = v.Id,
                    Name = v.Name,
                    DisplayName = v.DisplayName,
                    IsSystemRole = v.IsSystemRole,
                    CreateTime = v.CreateTime,
                    UpdateTime = v.UpdateTime
                }).ToList();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "用户角色读取失败: UserId={UserId}", userId); return Array.Empty<RoleEntity>(); }
        }

        public async Task AssignRoleAsync(long userId, long roleId, CancellationToken ct = default)
        {
            try
            {
                var exists = await UserRoleDataService.ExistsAsync(userId, roleId, ct);
                if (exists) return; // 幂等
                await UserRoleDataService.CreateAsync(new UserRoleEntity { UserId = userId, RoleId = roleId }, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "用户角色分配失败: UserId={UserId}, RoleId={RoleId}", userId, roleId); }
        }

        public async Task RemoveRoleAsync(long userId, long roleId, CancellationToken ct = default)
        {
            try { await UserRoleDataService.DeleteAsync(userId, roleId, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "用户角色移除失败: UserId={UserId}, RoleId={RoleId}", userId, roleId); }
        }
    }
}
