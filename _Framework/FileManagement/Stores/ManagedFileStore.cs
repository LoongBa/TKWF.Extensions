using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件存储实现（internal）——经 <see cref="ManagedFileEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——唯一约束冲突/业务规则违反由 Manager 处理；事务由 Manager 统一管理。</para>
    /// </summary>
    internal sealed class ManagedFileStore : IManagedFileStore
    {
        private readonly IDomainUser _user;
        private ManagedFileEntityDataService? _dataService;

        private ManagedFileEntityDataService DataService => _dataService ??= _user.Use<ManagedFileEntityDataService>();

        public ManagedFileStore(IDomainUser user)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public Task<ManagedFileEntity?> GetByIdAsync(long id, CancellationToken ct = default)
            => DataService.EntityGetAsync(f => f.Id == id, ct);

        public Task<ManagedFileEntity?> GetByFolderAndNameAsync(long? folderId, string name, CancellationToken ct = default)
            => DataService.GetByFolderAndNameAsync(folderId, name, ct);

        public Task<IReadOnlyList<ManagedFileEntity>> GetByFolderAsync(long? folderId, int skip, int take, CancellationToken ct = default)
            => DataService.GetByFolderAsync(folderId, skip, take, ct);

        public Task<IReadOnlyList<ManagedFileEntity>> GetBySha256Async(string sha256, int skip, int take, CancellationToken ct = default)
            => DataService.GetBySha256Async(sha256, skip, take, ct);

        public Task<IReadOnlyList<ManagedFileEntity>> SearchByNameAsync(string keyword, int skip, int take, CancellationToken ct = default)
            => DataService.SearchByNameAsync(keyword, skip, take, ct);

        public Task<long> CountByFolderIdAsync(long? folderId, CancellationToken ct = default)
            => DataService.CountByFolderIdAsync(folderId, ct);

        public Task<long> SumSizeByFolderIdAsync(long? folderId, CancellationToken ct = default)
            => DataService.SumSizeByFolderIdAsync(folderId, ct);

        public Task<long> SumSizeAllAsync(CancellationToken ct = default)
            => DataService.SumSizeAllAsync(ct);

        public Task<long> CountByOwnerAsync(long ownerId, CancellationToken ct = default)
            => DataService.CountByOwnerAsync(ownerId, ct);

        public Task<long> SumSizeByOwnerAsync(long ownerId, CancellationToken ct = default)
            => DataService.SumSizeByOwnerAsync(ownerId, ct);

        public async Task<long> CreateAsync(ManagedFileEntity entity, CancellationToken ct = default)
        {
            await DataService.CreateAsync(entity, ct);
            return entity.Id;
        }

        public Task UpdateAsync(ManagedFileEntity entity, CancellationToken ct = default)
            => DataService.UpdateAsync(entity, ct);

        public Task DeleteAsync(long id, CancellationToken ct = default)
            => DataService.EntityDeleteBatchAsync(new[] { id }, ct);
    }
}
