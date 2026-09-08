using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元存储实现（internal）——经 <see cref="OrganizationUnitEntityDataService"/> +
    /// <see cref="OrganizationUnitUserEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——唯一约束冲突由 Manager 捕获转业务异常；事务由 Manager 统一管理。</para>
    /// </summary>
    internal sealed class OrganizationUnitStore : IOrganizationUnitStore
    {
        private readonly OrganizationUnitEntityDataService _ouDataService;
        private readonly OrganizationUnitUserEntityDataService _userDataService;

        public OrganizationUnitStore(
            OrganizationUnitEntityDataService ouDataService,
            OrganizationUnitUserEntityDataService userDataService)
        {
            _ouDataService = ouDataService ?? throw new ArgumentNullException(nameof(ouDataService));
            _userDataService = userDataService ?? throw new ArgumentNullException(nameof(userDataService));
        }

        public Task<OrganizationUnitEntity?> GetByIdAsync(long id, CancellationToken ct = default)
            => _ouDataService.EntityGetAsync(o => o.Id == id, ct);

        public Task<OrganizationUnitEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
            => _ouDataService.GetByCodeAsync(code, ct);

        public Task<IReadOnlyList<OrganizationUnitEntity>> GetAllAsync(CancellationToken ct = default)
            => _ouDataService.GetAllAsync(ct);

        public async Task<long> CreateAsync(OrganizationUnitEntity entity, CancellationToken ct = default)
        {
            await _ouDataService.EntityCreateAsync(entity, ct);
            return entity.Id;
        }

        public Task UpdateAsync(OrganizationUnitEntity entity, CancellationToken ct = default)
            => _ouDataService.EntityUpdateAsync(entity, ct);

        public Task DeleteAsync(long id, CancellationToken ct = default)
            => _ouDataService.DeleteEntityAsync(id, ct);

        public Task AddUserAsync(OrganizationUnitUserEntity entity, CancellationToken ct = default)
            => _userDataService.AddAsync(entity, ct);

        public Task RemoveUserAsync(long ouId, string userId, CancellationToken ct = default)
            => _userDataService.RemoveAsync(ouId, userId, ct);

        public Task<IReadOnlyList<string>> GetUserIdsByOrganizationUnitIdsAsync(IReadOnlyList<long> ouIds, CancellationToken ct = default)
            => _userDataService.GetUserIdsByOrganizationUnitIdsAsync(ouIds, ct);

        public async Task<IReadOnlyList<long>> GetOrganizationUnitIdsForUserAsync(string userId, CancellationToken ct = default)
        {
            var rows = await _userDataService.GetByUserAsync(userId, ct);
            return rows.Select(r => r.OrganizationUnitId).Distinct().ToList();
        }

        public Task<long> CountUsersByOrganizationUnitIdAsync(long ouId, CancellationToken ct = default)
            => _userDataService.CountByOrganizationUnitIdAsync(ouId, ct);

        public Task DeleteUsersByOrganizationUnitIdsAsync(IReadOnlyList<long> ouIds, CancellationToken ct = default)
            => _userDataService.DeleteByOrganizationUnitIdsAsync(ouIds, ct);
    }
}
