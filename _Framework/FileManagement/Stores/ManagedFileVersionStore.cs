using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件版本存储实现（internal）——经 <see cref="ManagedFileVersionEntityDataService"/>（SG1/xCodeGen 生成的 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——版本号冲突由 Manager 处理；事务由 Manager 统一管理。</para>
    /// </summary>
    internal sealed class ManagedFileVersionStore : IManagedFileVersionStore
    {
        private readonly ManagedFileVersionEntityDataService _dataService;

        public ManagedFileVersionStore(ManagedFileVersionEntityDataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        }

        public Task<IReadOnlyList<ManagedFileVersionEntity>> GetByFileAsync(long fileId, CancellationToken ct = default)
            => _dataService.GetByFileAsync(fileId, ct);

        public Task<ManagedFileVersionEntity?> GetByFileAndVersionAsync(long fileId, int version, CancellationToken ct = default)
            => _dataService.GetByFileAndVersionAsync(fileId, version, ct);

        public Task<int> GetMaxVersionAsync(long fileId, CancellationToken ct = default)
            => _dataService.GetMaxVersionAsync(fileId, ct);

        public Task CreateAsync(ManagedFileVersionEntity entity, CancellationToken ct = default)
            => _dataService.CreateAsync(entity, ct);

        public Task DeleteByFileIdAsync(long fileId, CancellationToken ct = default)
            => _dataService.DeleteByFileIdAsync(fileId, ct);
    }
}
