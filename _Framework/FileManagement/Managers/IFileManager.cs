using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件管理门面（公开）——目录树 CRUD（引用守卫 + Level/Path 物化路径 + 删除保护 + 事务包裹）
    /// + 文件上传/下载/删除/重命名/移动（安全校验链 + SHA256 去重 + BlobStoring 委托 + 事务包裹）。
    /// <para>业务规则：Code 白名单 + 防穿越（F7/C6）、扩展名白名单 + 大小限制（F6/C4）、SHA256 去重（F5）、
    /// 目录删除保护（F2/D17）、目录重命名只改 Name（F3/C3）、ContentType 服务端推导（C5）、
    /// 唯一约束冲突转业务异常（P3）。</para>
    /// </summary>
    public interface IFileManager
    {
        // ── 目录 ──

        /// <summary>创建目录（Code 白名单 + 防穿越 + 父存在守卫 + Level/Path 物化路径 + SortOrder 默认同级末尾 + 事务包裹）。</summary>
        Task<FileFolderEntity> CreateFolderAsync(string code, string name, long? parentId = null, int? sortOrder = null, CancellationToken ct = default);

        /// <summary>更新目录（仅更新非空字段 Name/SortOrder + UpdateTime；<b>Code 不可改</b>——Path 不重算，C3）。</summary>
        Task UpdateFolderAsync(long id, string? name = null, int? sortOrder = null, CancellationToken ct = default);

        /// <summary>删除目录（删除保护：事务内二次确认子目录计数 + 文件计数均为 0 → 删；否则 InvalidOperationException，D17）。</summary>
        Task DeleteFolderAsync(long id, CancellationToken ct = default);

        /// <summary>重命名目录（校验新名安全 + 只改 Name——Path 由 Code 构建，子树 Path 不变，O(1)，C3）。</summary>
        Task RenameFolderAsync(long id, string newName, CancellationToken ct = default);

        /// <summary>全树组装（全量内存 nodeMap → childrenMap → 递归；SortOrder 升序；孤儿检测抛异常，对齐 OU BuildTree）。</summary>
        Task<FileFolderTreeNode> GetFolderTreeAsync(CancellationToken ct = default);

        /// <summary>按父目录查直接子目录（parentId=null → 根目录）。</summary>
        Task<IReadOnlyList<FileFolderEntity>> GetSubFoldersAsync(long? parentId, CancellationToken ct = default);

        // ── 文件 ──

        /// <summary>上传文件（校验链：非空 → 防穿越 → 扩展名白名单 → 大小限制（seekable fail-fast / 非 seekable 边复制计数）
        /// → SHA256 → 去重预查 → ContentType 服务端推导 → Blob 落盘 → 事务内元数据落库；失败 best-effort 清理 Blob，D4/D5/D16/D17）。</summary>
        Task<ManagedFileEntity> UploadFileAsync(long? folderId, string fileName, Stream content, string? contentType = null, CancellationToken ct = default);

        /// <summary>下载文件（元数据 GetById（不存在 → InvalidOperationException）→ Blob DownloadAsync(storedPath)
        /// （缺失 → FileNotFoundException，fail-fast）→ 返回 (Stream, 元数据)）。</summary>
        Task<(Stream Stream, ManagedFileEntity File)?> DownloadFileAsync(long id, CancellationToken ct = default);

        /// <summary>删除文件（事务包裹：删元数据 → Blob 删（失败 → 日志警告，孤儿容忍））。</summary>
        Task DeleteFileAsync(long id, CancellationToken ct = default);

        /// <summary>重命名文件（校验新名安全 + 目录内唯一约束冲突 → InvalidOperationException("该目录下已存在同名文件")，P3）。</summary>
        Task RenameFileAsync(long id, string newName, CancellationToken ct = default);

        /// <summary>移动文件到新目录（目标目录存在守卫 + 目标重名检查 + FolderId 更新；folderId 可空 = 移到根级）。</summary>
        Task MoveFileAsync(long id, long? newFolderId, CancellationToken ct = default);

        // ── 查询 ──

        /// <summary>按 Id 读取文件（不存在返回 null）。</summary>
        Task<ManagedFileEntity?> GetFileAsync(long id, CancellationToken ct = default);

        /// <summary>按目录分页查询文件（folderId 可空 → 根级文件；take 缺省用 Options.DefaultPageSize）。</summary>
        Task<IReadOnlyList<ManagedFileEntity>> GetFilesAsync(long? folderId, int skip = 0, int? take = null, CancellationToken ct = default);

        /// <summary>按文件名关键字模糊分页查询（take 缺省用 Options.DefaultPageSize）。</summary>
        Task<IReadOnlyList<ManagedFileEntity>> SearchFilesAsync(string keyword, int skip = 0, int? take = null, CancellationToken ct = default);
    }
}
