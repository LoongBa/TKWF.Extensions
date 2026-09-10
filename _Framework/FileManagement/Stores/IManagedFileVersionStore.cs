using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件版本存储抽象（internal，V0.2.0）——文件版本历史持久化操作，经 SG1 DataService 委托实现。
    /// <para>异常自然传播（不静默）：版本号冲突（UX_mfv_file_version 唯一约束）由 <see cref="IFileManager"/> 处理；
    /// 事务由 Manager 层统一管理（Store 不触碰 <c>ITransactionManager</c>）。</para>
    /// <para>数据访问红线（2026-09-07 用户裁定）：Store 不注入 IFreeSql / IEntityDAC——只依赖 DataService。</para>
    /// </summary>
    internal interface IManagedFileVersionStore
    {
        /// <summary>按文件查询全部版本（Version 升序；上限 1000——P2-6，对齐 PrintTemplates 先例）。</summary>
        Task<IReadOnlyList<ManagedFileVersionEntity>> GetByFileAsync(long fileId, CancellationToken ct = default);

        /// <summary>按文件 + 版本号查单个版本（不存在返回 null）。</summary>
        Task<ManagedFileVersionEntity?> GetByFileAndVersionAsync(long fileId, int version, CancellationToken ct = default);

        /// <summary>查询文件当前最大版本号（无版本返回 0——首版 1 的前置）。</summary>
        Task<int> GetMaxVersionAsync(long fileId, CancellationToken ct = default);

        /// <summary>新增版本行（UX_mfv_file_version 唯一约束兜底并发冲突）。</summary>
        Task CreateAsync(ManagedFileVersionEntity entity, CancellationToken ct = default);

        /// <summary>按文件物理删除全部版本行（文件删除清理，Oracle P1-2）。</summary>
        Task DeleteByFileIdAsync(long fileId, CancellationToken ct = default);
    }
}
