using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// Blob 记录存储抽象——定义 Blob 元数据记录的 CRUD 操作。
    /// <para>V0.1.0 FreeSql 默认实现；后续可扩展 EF Core 等。</para>
    /// </summary>
    public interface IBlobRecordStore
    {
        /// <summary>按 ID 读取单条 Blob 记录。</summary>
        Task<BlobRecordEntity?> GetAsync(long id, CancellationToken ct = default);

        /// <summary>按名称读取单条 Blob 记录。</summary>
        Task<BlobRecordEntity?> GetByNameAsync(string name, CancellationToken ct = default);

        /// <summary>读取 Blob 记录列表（按内容类型过滤、分页）。</summary>
        Task<IReadOnlyList<BlobRecordEntity>> GetListAsync(
            string? contentType = null,
            int skip = 0,
            int take = 20,
            CancellationToken ct = default);

        /// <summary>保存（插入/更新）Blob 记录。</summary>
        Task SaveAsync(BlobRecordEntity record, CancellationToken ct = default);

        /// <summary>删除 Blob 记录。</summary>
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
