using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V0.3.0（权限管理 Service）：权限授予 DataService——手写骨架（DMP-Lite 模式，`.cs` 入库）。
    /// <para>继承 <see cref="DomainDataServiceBase{TEntity, TDto}"/>（非泛型版，ADR42 D4），
    /// 提供标准 CRUD + 自定义查询（按权限名/按 provider）。</para>
    /// <para>消费方通过 DI 注入 <c>PermissionGrantEntityDataService</c> 即可操作权限授予数据。</para>
    /// <para>后续 xCodeGen 环境修复后，可生成 `.g.cs` 覆盖本骨架（`.cs` 优先级低于 `.g.cs`）。</para>
    /// </summary>
    public sealed class PermissionGrantEntityDataService
        : DomainDataServiceBase<PermissionGrantEntity, PermissionGrantDto>
    {
        private readonly IEntityDAC<PermissionGrantEntity> _dac;

        /// <summary>
        /// 构造函数——注入 <see cref="IDomainUser"/>（消费方上下文）+ <see cref="IEntityDAC{TEntity}"/>（ORM 无关）。
        /// </summary>
        public PermissionGrantEntityDataService(
            IDomainUser user,
            IEntityDAC<PermissionGrantEntity> dac)
            : base(user, dac, hasSoftDelete: false)
        {
            _dac = dac;
        }

        /// <summary>按权限名查询所有授予记录。</summary>
        public async Task<List<PermissionGrantDto>> GetByPermissionNameAsync(
            string permissionName, CancellationToken ct = default)
        {
            var query = _dac.Query.Where(g => g.PermissionName == permissionName);
            var entities = await _dac.ToListAsync(query, ct);
            return entities.Select(PermissionGrantDto.FromEntity).ToList();
        }

        /// <summary>按 provider（如 "User"/"Role"）查询所有授予记录。</summary>
        public async Task<List<PermissionGrantDto>> GetByProviderAsync(
            string providerName, string providerKey, CancellationToken ct = default)
        {
            var query = _dac.Query
                .Where(g => g.ProviderName == providerName && g.ProviderKey == providerKey);
            var entities = await _dac.ToListAsync(query, ct);
            return entities.Select(PermissionGrantDto.FromEntity).ToList();
        }

        /// <summary>查询指定权限名 + provider 的授予状态。</summary>
        public async Task<PermissionGrantDto?> GetGrantAsync(
            string permissionName, string providerName, string providerKey,
            CancellationToken ct = default)
        {
            var query = _dac.Query
                .Where(g => g.PermissionName == permissionName
                            && g.ProviderName == providerName
                            && g.ProviderKey == providerKey);
            var entity = await _dac.FirstOrDefaultAsync(query, ct);
            return entity == null ? null : PermissionGrantDto.FromEntity(entity);
        }

        /// <summary>
        /// 设置权限授予（upsert）——存在则更新 IsGranted，不存在则插入。
        /// <para>替代原 <see cref="IPermissionStore.SetAsync"/> 的编程式调用，提供 DataService 级封装。</para>
        /// </summary>
        public async Task<PermissionGrantDto> SetGrantAsync(
            string permissionName, string providerName, string providerKey,
            bool isGranted, CancellationToken ct = default)
        {
            var query = _dac.Query
                .Where(g => g.PermissionName == permissionName
                            && g.ProviderName == providerName
                            && g.ProviderKey == providerKey);
            var existing = await _dac.FirstOrDefaultAsync(query, ct);

            if (existing != null)
            {
                existing.IsGranted = isGranted;
                await _dac.UpdateAsync(existing, ct);
                return PermissionGrantDto.FromEntity(existing);
            }
            else
            {
                var entity = new PermissionGrantEntity
                {
                    PermissionName = permissionName,
                    ProviderName = providerName,
                    ProviderKey = providerKey,
                    IsGranted = isGranted
                };
                var inserted = await _dac.InsertAsync(entity, ct);
                return PermissionGrantDto.FromEntity(inserted);
            }
        }

        /// <summary>撤销权限授予（删除记录）。</summary>
        public async Task<bool> RevokeGrantAsync(
            string permissionName, string providerName, string providerKey,
            CancellationToken ct = default)
        {
            var query = _dac.Query
                .Where(g => g.PermissionName == permissionName
                            && g.ProviderName == providerName
                            && g.ProviderKey == providerKey);
            var existing = await _dac.FirstOrDefaultAsync(query, ct);
            if (existing == null) return false;

            await _dac.DeleteAsync(existing, ct);
            return true;
        }
    }
}
