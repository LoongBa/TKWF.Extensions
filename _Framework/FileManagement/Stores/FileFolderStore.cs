using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 目录存储实现（internal）——经 <see cref="FileFolderEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——唯一约束冲突/业务规则违反由 Manager 处理；事务由 Manager 统一管理。</para>
    /// </summary>
    internal sealed class FileFolderStore : IFileFolderStore
    {
        private readonly IDomainUser _user;
        private FileFolderEntityDataService? _dataService;

        private FileFolderEntityDataService DataService => _dataService ??= _user.Use<FileFolderEntityDataService>();

        public FileFolderStore(IDomainUser user)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public Task<FileFolderEntity?> GetByIdAsync(long id, CancellationToken ct = default)
            => DataService.EntityGetAsync(f => f.Id == id, ct);

        public Task<FileFolderEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
            => DataService.GetByCodeAsync(code, ct);

        public Task<IReadOnlyList<FileFolderEntity>> GetAllAsync(CancellationToken ct = default)
            => DataService.GetAllAsync(ct);

        public Task<IReadOnlyList<FileFolderEntity>> GetChildrenByParentIdAsync(long? parentId, CancellationToken ct = default)
            => DataService.GetChildrenByParentIdAsync(parentId, ct);

        public Task<long> CountChildrenByParentIdAsync(long? parentId, CancellationToken ct = default)
            => DataService.CountChildrenByParentIdAsync(parentId, ct);

        public async Task<long> CreateAsync(FileFolderEntity entity, CancellationToken ct = default)
        {
            await DataService.CreateAsync(entity, ct);
            return entity.Id;
        }

        public Task UpdateAsync(FileFolderEntity entity, CancellationToken ct = default)
            => DataService.UpdateAsync(entity, ct);

        public Task DeleteAsync(long id, CancellationToken ct = default)
            => DataService.EntityDeleteBatchAsync(new[] { id }, ct);
    }
}
