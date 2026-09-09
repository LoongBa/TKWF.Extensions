using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件存储实现（internal）——经 <see cref="ManagedFileEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——唯一约束冲突/业务规则违反由 Manager 处理；事务由 Manager 统一管理。</para>
    /// </summary>
    internal sealed class ManagedFileStore : IManagedFileStore
    {
        private readonly ManagedFileEntityDataService _dataService;

        public ManagedFileStore(ManagedFileEntityDataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        }

        public Task<ManagedFileEntity?> GetByIdAsync(long id, CancellationToken ct = default)
            => _dataService.EntityGetAsync(f => f.Id == id, ct);

        public Task<ManagedFileEntity?> GetByFolderAndNameAsync(long? folderId, string name, CancellationToken ct = default)
            => _dataService.GetByFolderAndNameAsync(folderId, name, ct);

        public Task<IReadOnlyList<ManagedFileEntity>> GetByFolderAsync(long? folderId, int skip, int take, CancellationToken ct = default)
            => _dataService.GetByFolderAsync(folderId, skip, take, ct);

        public Task<IReadOnlyList<ManagedFileEntity>> GetBySha256Async(string sha256, int skip, int take, CancellationToken ct = default)
            => _dataService.GetBySha256Async(sha256, skip, take, ct);

        public Task<IReadOnlyList<ManagedFileEntity>> SearchByNameAsync(string keyword, int skip, int take, CancellationToken ct = default)
            => _dataService.SearchByNameAsync(keyword, skip, take, ct);

        public Task<long> CountByFolderIdAsync(long? folderId, CancellationToken ct = default)
            => _dataService.CountByFolderIdAsync(folderId, ct);

        public async Task<long> CreateAsync(ManagedFileEntity entity, CancellationToken ct = default)
        {
            await _dataService.CreateAsync(entity, ct);
            return entity.Id;
        }

        public Task UpdateAsync(ManagedFileEntity entity, CancellationToken ct = default)
            => _dataService.UpdateAsync(entity, ct);

        public Task DeleteAsync(long id, CancellationToken ct = default)
            => _dataService.EntityDeleteBatchAsync(new[] { id }, ct);
    }
}
