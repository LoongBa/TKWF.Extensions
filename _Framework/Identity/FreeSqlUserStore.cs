using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// FreeSql 用户存储实现——将 <see cref="UserEntity"/> / <see cref="UserRoleEntity"/> 持久化到数据库。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。
    /// 与 FreeSqlSettingStore / FreeSqlBlobRecordStore 模式一致。</para>
    /// </summary>
    internal sealed class FreeSqlUserStore : IUserStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlUserStore> _logger;

        public FreeSqlUserStore(IFreeSql freeSql, ILogger<FreeSqlUserStore> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<UserEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<UserEntity>()
                    .Where(u => u.Id == id)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户读取失败: Id={Id}", id);
                return null;
            }
        }

        public async Task<UserEntity?> GetByUserNameAsync(string normalizedUserName, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<UserEntity>()
                    .Where(u => u.NormalizedUserName == normalizedUserName)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户按用户名读取失败: UserName={UserName}", normalizedUserName);
                return null;
            }
        }

        public async Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<UserEntity>()
                    .Where(u => u.Email == email)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户按邮箱读取失败: Email={Email}", email);
                return null;
            }
        }

        public async Task<IReadOnlyList<UserEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<UserEntity>()
                    .OrderByDescending(u => u.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户列表读取失败");
                return Array.Empty<UserEntity>();
            }
        }

        public async Task CreateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) return;

            try
            {
                var newId = await _freeSql.Insert(user).ExecuteIdentityAsync(ct);
                user.Id = newId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户创建失败: UserName={UserName}", user.UserName);
            }
        }

        public async Task UpdateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) return;

            try
            {
                user.UpdateTime = DateTimeOffset.Now;
                await _freeSql.Update<UserEntity>()
                    .SetSource(user)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户更新失败: Id={Id}", user.Id);
            }
        }

        public async Task DeleteAsync(long id, CancellationToken ct = default)
        {
            try
            {
                // 级联清理用户-角色映射
                await _freeSql.Delete<UserRoleEntity>()
                    .Where(ur => ur.UserId == id)
                    .ExecuteAffrowsAsync(ct);

                await _freeSql.Delete<UserEntity>()
                    .Where(u => u.Id == id)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户删除失败: Id={Id}", id);
            }
        }

        public async Task<IReadOnlyList<RoleEntity>> GetRolesAsync(long userId, CancellationToken ct = default)
        {
            try
            {
                var roleIds = await _freeSql.Select<UserRoleEntity>()
                    .Where(ur => ur.UserId == userId)
                    .ToListAsync(ur => ur.RoleId, ct);

                if (roleIds == null || roleIds.Count == 0)
                    return Array.Empty<RoleEntity>();

                return await _freeSql.Select<RoleEntity>()
                    .Where(r => roleIds.Contains(r.Id))
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户角色读取失败: UserId={UserId}", userId);
                return Array.Empty<RoleEntity>();
            }
        }

        public async Task AssignRoleAsync(long userId, long roleId, CancellationToken ct = default)
        {
            try
            {
                // 幂等：已存在则跳过
                var exists = await _freeSql.Select<UserRoleEntity>()
                    .AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct);
                if (exists) return;

                await _freeSql.Insert(new UserRoleEntity { UserId = userId, RoleId = roleId })
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户角色分配失败: UserId={UserId}, RoleId={RoleId}", userId, roleId);
            }
        }

        public async Task RemoveRoleAsync(long userId, long roleId, CancellationToken ct = default)
        {
            try
            {
                await _freeSql.Delete<UserRoleEntity>()
                    .Where(ur => ur.UserId == userId && ur.RoleId == roleId)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "用户角色移除失败: UserId={UserId}, RoleId={RoleId}", userId, roleId);
            }
        }
    }
}