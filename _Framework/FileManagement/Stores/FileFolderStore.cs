using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 目录存储实现（internal）——经 <see cref="FileFolderEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——唯一约束冲突/业务规则违反由 Manager 处理；事务由 Manager 统一管理。</para>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）：**内部接线型**（skill §4.2）——接口 internal
    /// （FileManager 内部组合依赖，<b>不可改接口可见性</b>，非 IDomainService），实现 ctor 改
    /// <see cref="IServiceProvider"/>（C1 延迟解析），DataService 经 <c>serviceProvider.GetRequiredService&lt;FileFolderEntityDataService&gt;()</c>
    /// 普通 DI 解析——修复真实生产故障：旧 ctor 注入 IDomainUser（<b>永不注册 DI</b>，D01）在 FileManager 工厂 lambda
    /// 经 <c>sp.GetRequiredService&lt;IFileFolderStore&gt;()</c> 解析时构造失败 → 生产解析必失败。</para>
    /// </summary>
    internal sealed class FileFolderStore : IFileFolderStore
    {
        private readonly IServiceProvider _serviceProvider;
        private FileFolderEntityDataService? _dataService;

        private FileFolderEntityDataService DataService => _dataService ??= _serviceProvider.GetRequiredService<FileFolderEntityDataService>();

        public FileFolderStore(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
