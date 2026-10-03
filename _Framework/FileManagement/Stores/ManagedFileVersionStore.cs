using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件版本存储实现（internal）——经 <see cref="ManagedFileVersionEntityDataService"/>（SG1/xCodeGen 生成的 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——版本号冲突由 Manager 处理；事务由 Manager 统一管理。</para>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）：**内部接线型**（skill §4.2）——接口 internal
    /// （FileManager 内部组合依赖，<b>不可改接口可见性</b>，非 IDomainService），实现 ctor 改
    /// <see cref="IServiceProvider"/>（C1 延迟解析），DataService 经 <c>serviceProvider.GetRequiredService&lt;ManagedFileVersionEntityDataService&gt;()</c>
    /// 普通 DI 解析——修复真实生产故障：旧 ctor 注入 IDomainUser（<b>永不注册 DI</b>，D01）在 FileManager 工厂 lambda
    /// 经 <c>sp.GetRequiredService&lt;IManagedFileVersionStore&gt;()</c> 解析时构造失败 → 生产解析必失败。</para>
    /// </summary>
    internal sealed class ManagedFileVersionStore : IManagedFileVersionStore
    {
        private readonly IServiceProvider _serviceProvider;
        private ManagedFileVersionEntityDataService? _dataService;

        private ManagedFileVersionEntityDataService DataService => _dataService ??= _serviceProvider.GetRequiredService<ManagedFileVersionEntityDataService>();

        public ManagedFileVersionStore(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public Task<IReadOnlyList<ManagedFileVersionEntity>> GetByFileAsync(long fileId, CancellationToken ct = default)
            => DataService.GetByFileAsync(fileId, ct);

        public Task<IReadOnlyList<string>> GetStoredPathsByFileAsync(long fileId, CancellationToken ct = default)
            => DataService.GetStoredPathsByFileAsync(fileId, ct);

        public Task<ManagedFileVersionEntity?> GetByFileAndVersionAsync(long fileId, int version, CancellationToken ct = default)
            => DataService.GetByFileAndVersionAsync(fileId, version, ct);

        public Task<int> GetMaxVersionAsync(long fileId, CancellationToken ct = default)
            => DataService.GetMaxVersionAsync(fileId, ct);

        public Task CreateAsync(ManagedFileVersionEntity entity, CancellationToken ct = default)
            => DataService.CreateAsync(entity, ct);

        public Task DeleteByFileIdAsync(long fileId, CancellationToken ct = default)
            => DataService.DeleteByFileIdAsync(fileId, ct);
    }
}
