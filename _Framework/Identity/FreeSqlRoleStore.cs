using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// FreeSql 角色存储实现——将 <see cref="RoleEntity"/> 持久化到数据库。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。
    /// 与 FreeSqlSettingStore / FreeSqlBlobRecordStore 模式一致。</para>
    /// </summary>
    internal sealed class FreeSqlRoleStore : IRoleStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlRoleStore> _logger;

        public FreeSqlRoleStore(IFreeSql freeSql, ILogger<FreeSqlRoleStore> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<RoleEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<RoleEntity>()
                    .Where(r => r.Id == id)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "角色读取失败: Id={Id}", id);
                return null;
            }
        }

        public async Task<RoleEntity?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<RoleEntity>()
                    .Where(r => r.Name == name)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "角色按名称读取失败: Name={Name}", name);
                return null;
            }
        }

        public async Task<IReadOnlyList<RoleEntity>> GetListAsync(int skip = 0, int take = 20, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<RoleEntity>()
                    .OrderByDescending(r => r.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "角色列表读取失败");
                return Array.Empty<RoleEntity>();
            }
        }

        public async Task CreateAsync(RoleEntity role, CancellationToken ct = default)
        {
            if (role == null) return;

            try
            {
                var newId = await _freeSql.Insert(role).ExecuteIdentityAsync(ct);
                role.Id = newId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "角色创建失败: Name={Name}", role.Name);
            }
        }

        public async Task UpdateAsync(RoleEntity role, CancellationToken ct = default)
        {
            if (role == null) return;

            try
            {
                role.UpdateTime = DateTimeOffset.Now;
                await _freeSql.Update<RoleEntity>()
                    .SetSource(role)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "角色更新失败: Id={Id}", role.Id);
            }
        }

        public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
        {
            try
            {
                var role = await GetByIdAsync(id, ct);
                if (role == null) return false;

                // 系统角色不可删除
                if (role.IsSystemRole) return false;

                // 已分配用户的角色不可删除
                var assigned = await _freeSql.Select<UserRoleEntity>()
                    .AnyAsync(ur => ur.RoleId == id, ct);
                if (assigned) return false;

                await _freeSql.Delete<RoleEntity>()
                    .Where(r => r.Id == id)
                    .ExecuteAffrowsAsync(ct);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "角色删除失败: Id={Id}", id);
                return false;
            }
        }
    }
}