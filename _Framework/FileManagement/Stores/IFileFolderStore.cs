using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 目录存储抽象（internal）——目录持久化操作，经 SG1 DataService 委托实现。
    /// <para>异常自然传播（不静默）：唯一约束冲突/业务异常由 <see cref="IFileManager"/> 处理；
    /// 事务由 Manager 层统一管理（Store 不触碰 <c>ITransactionManager</c>）。</para>
    /// <para>数据访问红线（2026-09-07 用户裁定）：Store 不注入 IFreeSql / IEntityDAC——只依赖 DataService。</para>
    /// </summary>
    internal interface IFileFolderStore
    {
        /// <summary>按 Id 读取目录（不存在返回 null）。</summary>
        Task<FileFolderEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>按 Code 读取目录（不存在返回 null）。</summary>
        Task<FileFolderEntity?> GetByCodeAsync(string code, CancellationToken ct = default);

        /// <summary>全量读取（Level/SortOrder 排序；树组装/同级 max+1 计算用）。</summary>
        Task<IReadOnlyList<FileFolderEntity>> GetAllAsync(CancellationToken ct = default);

        /// <summary>按父目录查直接子目录（parentId=null → 根目录）。</summary>
        Task<IReadOnlyList<FileFolderEntity>> GetChildrenByParentIdAsync(long? parentId, CancellationToken ct = default);

        /// <summary>按父目录统计直接子目录数（删除保护计数；parentId=null → 根目录计数）。</summary>
        Task<long> CountChildrenByParentIdAsync(long? parentId, CancellationToken ct = default);

        /// <summary>新增目录（回写自增 Id，返回 Id）。</summary>
        Task<long> CreateAsync(FileFolderEntity entity, CancellationToken ct = default);

        /// <summary>更新目录（全字段更新）。</summary>
        Task UpdateAsync(FileFolderEntity entity, CancellationToken ct = default);

        /// <summary>物理删除目录（调用方确保无子目录且无文件——删除保护由 Manager 层强制）。</summary>
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
