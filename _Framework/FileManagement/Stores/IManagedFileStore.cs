using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件存储抽象（internal）——文件元数据持久化操作，经 SG1 DataService 委托实现。
    /// <para>异常自然传播（不静默）：唯一约束冲突/业务异常由 <see cref="IFileManager"/> 处理；
    /// 事务由 Manager 层统一管理（Store 不触碰 <c>ITransactionManager</c>）。</para>
    /// <para>数据访问红线（2026-09-07 用户裁定）：Store 不注入 IFreeSql / IEntityDAC——只依赖 DataService。</para>
    /// </summary>
    internal interface IManagedFileStore
    {
        /// <summary>按 Id 读取文件（不存在返回 null）。</summary>
        Task<ManagedFileEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>按目录 + 文件名读取文件（UX 唯一约束双条件；folderId 可空 → 根级文件；不存在返回 null）。</summary>
        Task<ManagedFileEntity?> GetByFolderAndNameAsync(long? folderId, string name, CancellationToken ct = default);

        /// <summary>按目录分页查询（folderId 可空 → 根级文件；Name → Id 排序）。</summary>
        Task<IReadOnlyList<ManagedFileEntity>> GetByFolderAsync(long? folderId, int skip, int take, CancellationToken ct = default);

        /// <summary>按内容哈希分页查询（去重预查）。</summary>
        Task<IReadOnlyList<ManagedFileEntity>> GetBySha256Async(string sha256, int skip, int take, CancellationToken ct = default);

        /// <summary>按文件名关键字模糊分页查询。</summary>
        Task<IReadOnlyList<ManagedFileEntity>> SearchByNameAsync(string keyword, int skip, int take, CancellationToken ct = default);

        /// <summary>按目录统计文件数（删除保护计数；folderId 可空 → 根级文件计数）。</summary>
        Task<long> CountByFolderIdAsync(long? folderId, CancellationToken ct = default);

        /// <summary>新增文件（回写自增 Id，返回 Id）。</summary>
        Task<long> CreateAsync(ManagedFileEntity entity, CancellationToken ct = default);

        /// <summary>更新文件（全字段更新）。</summary>
        Task UpdateAsync(ManagedFileEntity entity, CancellationToken ct = default);

        /// <summary>物理删除文件元数据（调用方确保 Blob 已清理或容忍孤儿——Manager 层处理）。</summary>
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
