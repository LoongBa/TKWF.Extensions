using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元存储实现（internal）——经 <see cref="OrganizationUnitEntityDataService"/> +
    /// <see cref="OrganizationUnitUserEntityDataService"/> + <see cref="UserOrganizationUnitViewDataService"/>
    /// （SG1/xCodeGen 生成的 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：
    /// 扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——唯一约束冲突由 Manager 捕获转业务异常；事务由 Manager 统一管理。</para>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）：**内部接线型**（skill §4.2）——接口 internal
    /// （<see cref="IOrganizationUnitManager"/> 内部组合依赖，<b>不可改接口可见性</b>，非 IDomainService），
    /// 实现 ctor 改 <see cref="IServiceProvider"/>（C1 延迟解析），DataService 经
    /// <c>serviceProvider.GetRequiredService&lt;XxxDataService&gt;()</c> 普通 DI 解析——修复真实生产故障：
    /// 旧 ctor 注入 IDomainUser（<b>永不注册 DI</b>，D01）在 Manager 工厂经
    /// <c>sp.GetRequiredService&lt;IOrganizationUnitStore&gt;()</c> 解析时构造失败 → 生产解析必失败。</para>
    /// </summary>
    internal sealed class OrganizationUnitStore : IOrganizationUnitStore
    {
        private readonly IServiceProvider _serviceProvider;
        private OrganizationUnitEntityDataService? _ouDataService;
        private OrganizationUnitUserEntityDataService? _userDataService;
        private UserOrganizationUnitViewDataService? _userViewDataService;

        private OrganizationUnitEntityDataService OuDataService => _ouDataService ??= _serviceProvider.GetRequiredService<OrganizationUnitEntityDataService>();
        private OrganizationUnitUserEntityDataService UserDataService => _userDataService ??= _serviceProvider.GetRequiredService<OrganizationUnitUserEntityDataService>();
        private UserOrganizationUnitViewDataService UserViewDataService => _userViewDataService ??= _serviceProvider.GetRequiredService<UserOrganizationUnitViewDataService>();

        public OrganizationUnitStore(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public Task<OrganizationUnitEntity?> GetByIdAsync(long id, CancellationToken ct = default)
            => OuDataService.EntityGetAsync(o => o.Id == id, ct);

        public Task<OrganizationUnitEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
            => OuDataService.GetByCodeAsync(code, ct);

        public Task<IReadOnlyList<OrganizationUnitEntity>> GetAllAsync(CancellationToken ct = default)
            => OuDataService.GetAllAsync(ct);

        public Task<IReadOnlyList<OrganizationUnitEntity>> GetByPathPrefixAsync(string pathPrefix, CancellationToken ct = default)
            => OuDataService.GetByPathPrefixAsync(pathPrefix, ct);

        public Task<IReadOnlyList<OrganizationUnitEntity>> GetByCodesAsync(IReadOnlyList<string> codes, CancellationToken ct = default)
            => OuDataService.GetByCodesAsync(codes, ct);

        public async Task<IReadOnlyList<string>> GetUserIdsByOuPathPrefixAsync(string ouPathPrefix, CancellationToken ct = default)
        {
            var rows = await UserViewDataService.GetByOuPathPrefixAsync(ouPathPrefix, ct);
            return rows.Select(r => r.UserId).Distinct().ToList();
        }

        public async Task<long> CreateAsync(OrganizationUnitEntity entity, CancellationToken ct = default)
        {
            await OuDataService.EntityCreateAsync(entity, ct);
            return entity.Id;
        }

        public Task UpdateAsync(OrganizationUnitEntity entity, CancellationToken ct = default)
            => OuDataService.EntityUpdateAsync(entity, ct);

        public Task DeleteAsync(long id, CancellationToken ct = default)
            => OuDataService.DeleteEntityAsync(id, ct);

        public Task AddUserAsync(OrganizationUnitUserEntity entity, CancellationToken ct = default)
            => UserDataService.AddAsync(entity, ct);

        public Task RemoveUserAsync(long ouId, string userId, CancellationToken ct = default)
            => UserDataService.RemoveAsync(ouId, userId, ct);

        public Task<IReadOnlyList<string>> GetUserIdsByOrganizationUnitIdsAsync(IReadOnlyList<long> ouIds, CancellationToken ct = default)
            => UserDataService.GetUserIdsByOrganizationUnitIdsAsync(ouIds, ct);

        public async Task<IReadOnlyList<long>> GetOrganizationUnitIdsForUserAsync(string userId, CancellationToken ct = default)
        {
            var rows = await UserDataService.GetByUserAsync(userId, ct);
            return rows.Select(r => r.OrganizationUnitId).Distinct().ToList();
        }

        public Task<long> CountUsersByOrganizationUnitIdAsync(long ouId, CancellationToken ct = default)
            => UserDataService.CountByOrganizationUnitIdAsync(ouId, ct);

        public Task DeleteUsersByOrganizationUnitIdsAsync(IReadOnlyList<long> ouIds, CancellationToken ct = default)
            => UserDataService.DeleteByOrganizationUnitIdsAsync(ouIds, ct);
    }
}
