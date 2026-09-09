using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 目录存储实现（internal）——经 <see cref="FileFolderEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——唯一约束冲突/业务规则违反由 Manager 处理；事务由 Manager 统一管理。</para>
    /// </summary>
    internal sealed class FileFolderStore : IFileFolderStore
    {
        private readonly FileFolderEntityDataService _dataService;

        public FileFolderStore(FileFolderEntityDataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        }

        public Task<FileFolderEntity?> GetByIdAsync(long id, CancellationToken ct = default)
            => _dataService.EntityGetAsync(f => f.Id == id, ct);

        public Task<FileFolderEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
            => _dataService.GetByCodeAsync(code, ct);

        public Task<IReadOnlyList<FileFolderEntity>> GetAllAsync(CancellationToken ct = default)
            => _dataService.GetAllAsync(ct);

        public Task<IReadOnlyList<FileFolderEntity>> GetChildrenByParentIdAsync(long? parentId, CancellationToken ct = default)
            => _dataService.GetChildrenByParentIdAsync(parentId, ct);

        public Task<long> CountChildrenByParentIdAsync(long? parentId, CancellationToken ct = default)
            => _dataService.CountChildrenByParentIdAsync(parentId, ct);

        public async Task<long> CreateAsync(FileFolderEntity entity, CancellationToken ct = default)
        {
            await _dataService.CreateAsync(entity, ct);
            return entity.Id;
        }

        public Task UpdateAsync(FileFolderEntity entity, CancellationToken ct = default)
            => _dataService.UpdateAsync(entity, ct);

        public Task DeleteAsync(long id, CancellationToken ct = default)
            => _dataService.EntityDeleteBatchAsync(new[] { id }, ct);
    }
}
