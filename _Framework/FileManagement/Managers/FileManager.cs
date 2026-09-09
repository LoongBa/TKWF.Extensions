using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.BlobStoring;

namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件管理实现（public）——目录树生命周期 + 文件上传/下载/删除/重命名/移动门面。
    /// <para>事务包裹（对齐 OU/Approval/Calendar 范式）：目录 Create/Delete、文件 Upload/Delete 多步写经
    /// <see cref="ITransactionManager"/> BeginAsync → 业务 → CommitAsync / 失败 RollbackAsync（using scope）。</para>
    /// <para>物理存储委托：文件字节进出全部经 <see cref="IBlobStorageService"/>（BlobStoring.Abstractions 契约，C1/ADR50 L2）——
    /// 本扩展<b>不</b>直接触碰文件系统/ORM（红线合规）；<c>ManagedFileEntity</c> 为唯一业务元数据（P2 裁定，不注入 IBlobRecordStore）。</para>
    /// <para>上传校验链（D4/D5 评审修订）：非空 → 防穿越 → 扩展名白名单 → 大小限制（seekable fail-fast / 非 seekable 边复制计数）
    /// → SHA256 → 去重预查 → ContentType 服务端推导（C5）→ Blob 落盘 → 事务内元数据落库 → 失败 best-effort 清理 Blob（D16/D17）。</para>
    /// <para>类为 public 但构造函数 internal（Store 为 internal 契约）——
    /// 由 <see cref="FileManagementExtensionInitializer{TUserInfo}.ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
    /// 以工厂方式 TryAddScoped 注册（消费方仍可自定义实现优先）。</para>
    /// </summary>
    public sealed class FileManager : IFileManager
    {
        private readonly IFileFolderStore _folderStore;
        private readonly IManagedFileStore _fileStore;
        private readonly IBlobStorageService _blobStorage;
        private readonly ITransactionManager _transactionManager;
        private readonly FileManagementOptions _options;
        private readonly ILogger<FileManager> _logger;

        /// <summary>物化路径最大长度（对齐实体列 MaxLength(1024)，对齐 OU C3）。</summary>
        private const int MaxPathLength = 1024;

        /// <summary>Code 白名单（对齐 OU C3）：字母/数字/下划线/点/连字符——保证 Path 段安全。</summary>
        private static readonly Regex CodeRegex = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

        /// <summary>去重预查单次取数上限（SHA256 命中量通常极小）。</summary>
        private const int DedupLookupSize = 200;

        /// <summary>允许扩展名（构造时小写归一——C6/P3：配置 `[".JPG"]` 与上传 `.jpg` 一致）。</summary>
        private readonly HashSet<string> _allowedExtensions;

        internal FileManager(
            IFileFolderStore folderStore,
            IManagedFileStore fileStore,
            IBlobStorageService blobStorage,
            ITransactionManager transactionManager,
            IOptions<FileManagementOptions> options,
            ILogger<FileManager> logger)
        {
            _folderStore = folderStore ?? throw new ArgumentNullException(nameof(folderStore));
            _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
            _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
            var opts = (options ?? throw new ArgumentNullException(nameof(options))).Value;
            _options = opts;
            _allowedExtensions = opts.AllowedExtensions
                .Select(e => e.Trim().ToLowerInvariant())
                .Where(e => e.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ── 目录 ──

        /// <inheritdoc />
        public async Task<FileFolderEntity> CreateFolderAsync(string code, string name, long? parentId = null,
            int? sortOrder = null, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!CodeRegex.IsMatch(code))
                throw new ArgumentException("Code 仅允许字母、数字、下划线、点、连字符（A-Za-z0-9_.-）", nameof(code));
            // 拒绝纯点组合（"."/".." 在 Path 段中语义怪异——对齐 OU P6）
            if (code is "." or "..")
                throw new ArgumentException("Code 不允许为纯点组合（. / ..）", nameof(code));
            EnsureSafeName(name, nameof(name));

            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                // 父存在校验 + 路径不变量（事务内，对齐 OU C1）
                int targetLevel;
                string targetPath;
                if (parentId.HasValue)
                {
                    var parent = await _folderStore.GetByIdAsync(parentId.Value, ct);
                    if (parent == null)
                        throw new InvalidOperationException($"父目录 {parentId.Value} 不存在");

                    targetLevel = parent.Level + 1;
                    targetPath = parent.Path + code + "/";
                }
                else
                {
                    targetLevel = 0;
                    targetPath = "/" + code + "/";
                }

                // Path 长度守卫——超限抛业务异常而非 DB OverflowError（对齐 OU C3）
                if (targetPath.Length > MaxPathLength)
                    throw new InvalidOperationException("目录层级过深或 Code 过长（Path 超过 1024 字符上限）");

                // SortOrder 默认同级末尾 max+1
                int finalSortOrder;
                if (sortOrder.HasValue)
                {
                    finalSortOrder = sortOrder.Value;
                }
                else
                {
                    var all = await _folderStore.GetAllAsync(ct);
                    finalSortOrder = (all.Where(f => f.ParentId == parentId)
                        .Select(f => (int?)f.SortOrder).Max() ?? -1) + 1;
                }

                var entity = new FileFolderEntity
                {
                    Code = code,
                    Name = name,
                    ParentId = parentId,
                    Level = targetLevel,
                    Path = targetPath,
                    SortOrder = finalSortOrder,
                    CreateTime = DateTime.UtcNow,
                    UpdateTime = DateTime.UtcNow
                };

                // 并发同 Code 冲突由数据库唯一索引异常自然传播（败者显式异常，对齐 OU/Approval 先例）
                await _folderStore.CreateAsync(entity, ct);

                await scope.CommitAsync(ct);
                return entity;
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task UpdateFolderAsync(long id, string? name = null, int? sortOrder = null, CancellationToken ct = default)
        {
            var folder = await _folderStore.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"目录 {id} 不存在");

            // Code 不可改（C3——Path 由 Code 构建，更新不触发路径重算）
            if (name != null)
            {
                EnsureSafeName(name, nameof(name));
                folder.Name = name;
            }
            if (sortOrder.HasValue) folder.SortOrder = sortOrder.Value;
            folder.UpdateTime = DateTime.UtcNow;

            await _folderStore.UpdateAsync(folder, ct);
        }

        /// <inheritdoc />
        public async Task DeleteFolderAsync(long id, CancellationToken ct = default)
        {
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                var folder = await _folderStore.GetByIdAsync(id, ct)
                    ?? throw new InvalidOperationException($"目录 {id} 不存在");

                // 删除保护（F2/D17）：事务内二次确认——子目录计数 + 文件计数均为 0 → 删；否则拒绝
                long childCount = await _folderStore.CountChildrenByParentIdAsync(id, ct);
                if (childCount > 0)
                    throw new InvalidOperationException($"目录含 {childCount} 个子目录，请先删除");

                long fileCount = await _fileStore.CountByFolderIdAsync(id, ct);
                if (fileCount > 0)
                    throw new InvalidOperationException($"目录含 {fileCount} 个文件，请先删除");

                await _folderStore.DeleteAsync(id, ct);

                await scope.CommitAsync(ct);
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task RenameFolderAsync(long id, string newName, CancellationToken ct = default)
        {
            // 新名安全校验（防穿越 + 非空）——对齐 C3：只改 Name，子树 Path 不变
            EnsureSafeName(newName, nameof(newName));

            var folder = await _folderStore.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"目录 {id} 不存在");

            folder.Name = newName;
            folder.UpdateTime = DateTime.UtcNow;
            await _folderStore.UpdateAsync(folder, ct);
        }

        /// <inheritdoc />
        public async Task<FileFolderTreeNode> GetFolderTreeAsync(CancellationToken ct = default)
        {
            var all = await _folderStore.GetAllAsync(ct);
            if (all.Count == 0)
                return new FileFolderTreeNode(0, "", "根", 0, Array.Empty<FileFolderTreeNode>());

            // 第一遍：Id → 节点映射（Children 占位为空，叶子由后续递归填充）
            var nodeMap = all.ToDictionary(f => f.Id,
                f => new FileFolderTreeNode(
                    f.Id, f.Code, f.Name, f.SortOrder, Array.Empty<FileFolderTreeNode>()));

            // 第二遍：ParentId 归类；孤儿（父不存在）抛异常（对齐 OU——不静默归根）
            var roots = new List<FileFolderTreeNode>();
            var childrenMap = new Dictionary<long, List<FileFolderTreeNode>>();

            foreach (var folder in all)
            {
                var node = nodeMap[folder.Id];
                if (!folder.ParentId.HasValue)
                {
                    roots.Add(node);
                }
                else if (!nodeMap.ContainsKey(folder.ParentId.Value))
                {
                    throw new InvalidOperationException(
                        $"目录 {folder.Code}(Id={folder.Id}) 的父节点 Id={folder.ParentId.Value} 不存在（数据异常，请检查）");
                }
                else if (!childrenMap.TryGetValue(folder.ParentId.Value, out var siblings))
                {
                    childrenMap[folder.ParentId.Value] = new List<FileFolderTreeNode> { node };
                }
                else
                {
                    siblings.Add(node);
                }
            }

            // 第三遍：递归挂接 Children，根与子级均按 SortOrder 升序。
            // 环检测说明（对齐 OU 审核 P4）：ParentId 单父约束 + 根节点（ParentId=null）不出现于任何
            // childrenMap 值——从根出发的父子链唯一确定且数学上不可能成环；Path≤1024 守卫亦间接限定树深，递归栈安全。
            roots.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            var assembled = new List<FileFolderTreeNode>(roots.Count);
            foreach (var root in roots)
                assembled.Add(AttachChildren(root, childrenMap));

            return new FileFolderTreeNode(0, "", "根", 0, assembled.AsReadOnly());
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<FileFolderEntity>> GetSubFoldersAsync(long? parentId, CancellationToken ct = default)
            => _folderStore.GetChildrenByParentIdAsync(parentId, ct);

        // ── 文件 ──

        /// <inheritdoc />
        public async Task<ManagedFileEntity> UploadFileAsync(long? folderId, string fileName, Stream content,
            string? contentType = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);   // 先于防穿越（C6——GetFileName("")=="" 可绕过）
            EnsureSafeFileName(fileName, nameof(fileName));

            // 扩展名白名单（小写归一）
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!_options.AllowAnyExtension && !_allowedExtensions.Contains(extension))
                throw new ArgumentException($"扩展名 \"{extension}\" 不在允许列表内（AllowAnyExtension=false）", nameof(fileName));

            // 限长副本（C4 双路径）：seekable → Length 超限 fail-fast（不读流）；非 seekable → 边复制边计数中断
            using var copy = new MemoryStream();
            await CopyBoundedAsync(content, copy, _options.MaxFileSizeBytes, ct);
            copy.Position = 0;

            // SHA256（DataPort 先例——对限长副本流式计算，副本受 Max 有界）
            string sha256 = await ComputeSha256Async(copy, ct);
            copy.Position = 0;

            // 去重（Deduplicate）：同 SHA256 命中同目录同名 → 幂等返回既有（仅关预查优化，UX 唯一约束仍在）
            if (_options.Deduplicate)
            {
                var dedupMatches = await _fileStore.GetBySha256Async(sha256, 0, DedupLookupSize, ct);
                var existing = dedupMatches.FirstOrDefault(f => f.FolderId == folderId && f.Name == fileName);
                if (existing != null)
                {
                    _logger.LogInformation("上传去重命中：文件 {Id} 已存在（同 SHA256 同目录同名），幂等返回", existing.Id);
                    return existing;
                }
            }

            // C2 根级同名预检 + P5 目录存在预检（前移 Blob 上传之前——避免先写物理再补偿的浪费 IO；
            // 事务内保留二次校验作并发兜底）：根级（folderId=null）可空唯一索引 NULL 语义
            // （SQLite/PostgreSQL 视 NULL 互不相同）——同名唯一由应用层保证，对齐 MoveFileAsync 目标重名检查
            if (folderId.HasValue)
            {
                var folder = await _folderStore.GetByIdAsync(folderId.Value, ct)
                    ?? throw new InvalidOperationException($"目录 {folderId.Value} 不存在");
            }
            else if (await _fileStore.GetByFolderAndNameAsync(null, fileName, ct) != null)
            {
                throw new InvalidOperationException("该目录下已存在同名文件");   // 根级同名（应用层唯一保证，C2）
            }

            // ContentType 服务端推导（C5）：由扩展名经 MIME 映射推导——不信任客户端传入值，未知 fallback application/octet-stream
            string resolvedContentType = FileManagementMimeMap.TryGet(extension) ?? "application/octet-stream";

            // 先写物理（Blob）——上传失败返回 null → fail-fast
            BlobInfo? blob = await _blobStorage.UploadAsync(fileName, copy, resolvedContentType, ct);
            if (blob == null)
                throw new InvalidOperationException("Blob 存储不可用（上传失败）");

            // 元数据落库（事务包裹）——任何失败 best-effort 清理 Blob + rethrow
            try
            {
                using var scope = await _transactionManager.BeginAsync(ct: ct);
                try
                {
                    // 并发兜底（P5）：事务内二次校验目录存在（预检前移后仅剩并发竞态窗口）
                    if (folderId.HasValue)
                    {
                        var folder = await _folderStore.GetByIdAsync(folderId.Value, ct)
                            ?? throw new InvalidOperationException($"目录 {folderId.Value} 不存在");
                    }

                    var entity = new ManagedFileEntity
                    {
                        FolderId = folderId,
                        Name = fileName,
                        StoredPath = blob.Path,
                        Extension = extension,
                        ContentType = resolvedContentType,
                        Size = blob.Size > 0 ? blob.Size : copy.Length,
                        Sha256 = sha256,
                        UploaderName = null,   // v0.1.0 预留（Manager 未注入当前用户上下文）
                        CreateTime = DateTime.UtcNow,
                        UpdateTime = DateTime.UtcNow
                    };

                    await _fileStore.CreateAsync(entity, ct);

                    await scope.CommitAsync(ct);
                    return entity;
                }
                catch
                {
                    await scope.RollbackAsync(ct);
                    throw;
                }
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                // 并发同目录同名上传（D17）：UX 唯一约束败者——补偿删除 Blob + 转业务异常
                await TryCleanupBlobAsync(blob.Path, ct);
                throw new InvalidOperationException("该目录下已存在同名文件", ex);
            }
            catch
            {
                // 元数据落库其他失败（D16）：best-effort 清理 Blob + rethrow
                await TryCleanupBlobAsync(blob.Path, ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<(Stream Stream, ManagedFileEntity File)?> DownloadFileAsync(long id, CancellationToken ct = default)
        {
            // 元数据定位（不存在 → fail-fast）
            var file = await _fileStore.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"文件 {id} 不存在");

            // 委托 Blob 读流（缺失 → fail-fast）
            var stream = await _blobStorage.DownloadAsync(file.StoredPath, ct);
            if (stream == null)
                throw new FileNotFoundException($"文件内容不存在（Blob 缺失）：{file.StoredPath}", file.StoredPath);

            return (stream, file);
        }

        /// <inheritdoc />
        public async Task DeleteFileAsync(long id, CancellationToken ct = default)
        {
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                var file = await _fileStore.GetByIdAsync(id, ct)
                    ?? throw new InvalidOperationException($"文件 {id} 不存在");

                // 删元数据 → 删 Blob（Blob 删失败/返回 false → 日志警告，孤儿容忍——不阻塞事务提交）
                await _fileStore.DeleteAsync(id, ct);
                try
                {
                    bool deleted = await _blobStorage.DeleteAsync(file.StoredPath, ct);
                    if (!deleted)
                        _logger.LogWarning("删除 Blob 返回 false（孤儿容忍）：{StoredPath}", file.StoredPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "删除 Blob 失败（孤儿容忍）：{StoredPath}", file.StoredPath);
                }

                await scope.CommitAsync(ct);
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task RenameFileAsync(long id, string newName, CancellationToken ct = default)
        {
            EnsureSafeFileName(newName, nameof(newName));

            var file = await _fileStore.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"文件 {id} 不存在");

            // C1：重算派生列——Extension/ContentType 由新扩展名服务端推导（对齐 C5 不变量）
            string newExtension = Path.GetExtension(newName).ToLowerInvariant();
            if (!_options.AllowAnyExtension && !_allowedExtensions.Contains(newExtension))
                throw new ArgumentException($"扩展名 \"{newExtension}\" 不在允许列表内（AllowAnyExtension=false）", nameof(newName));
            file.Extension = newExtension;
            file.ContentType = FileManagementMimeMap.TryGet(newExtension) ?? "application/octet-stream";

            file.Name = newName;
            file.UpdateTime = DateTime.UtcNow;

            try
            {
                await _fileStore.UpdateAsync(file, ct);
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                // P3：目录内同名唯一约束冲突 → 业务异常
                throw new InvalidOperationException("该目录下已存在同名文件", ex);
            }
        }

        /// <inheritdoc />
        public async Task MoveFileAsync(long id, long? newFolderId, CancellationToken ct = default)
        {
            var file = await _fileStore.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"文件 {id} 不存在");

            // 目标目录存在守卫
            if (newFolderId.HasValue)
            {
                var folder = await _folderStore.GetByIdAsync(newFolderId.Value, ct)
                    ?? throw new InvalidOperationException($"目标目录 {newFolderId.Value} 不存在");
            }

            // 目标重名检查（仅跨目录移动时——同目录移动 = no-op 允许）
            if (file.FolderId != newFolderId)
            {
                var sameName = await _fileStore.GetByFolderAndNameAsync(newFolderId, file.Name, ct);
                if (sameName != null)
                    throw new InvalidOperationException("该目录下已存在同名文件");
            }

            file.FolderId = newFolderId;
            file.UpdateTime = DateTime.UtcNow;

            try
            {
                await _fileStore.UpdateAsync(file, ct);
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                // P3：并发竞态下的唯一约束冲突（预查与写入之间）→ 业务异常
                throw new InvalidOperationException("该目录下已存在同名文件", ex);
            }
        }

        // ── 查询 ──

        /// <inheritdoc />
        public Task<ManagedFileEntity?> GetFileAsync(long id, CancellationToken ct = default)
            => _fileStore.GetByIdAsync(id, ct);

        /// <inheritdoc />
        public Task<IReadOnlyList<ManagedFileEntity>> GetFilesAsync(long? folderId, int skip = 0, int? take = null, CancellationToken ct = default)
        {
            int pageSize = take ?? Math.Max(1, _options.DefaultPageSize);
            return _fileStore.GetByFolderAsync(folderId, skip, pageSize, ct);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ManagedFileEntity>> SearchFilesAsync(string keyword, int skip = 0, int? take = null, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
            int pageSize = take ?? Math.Max(1, _options.DefaultPageSize);
            return _fileStore.SearchByNameAsync(keyword, skip, pageSize, ct);
        }

        // ── 内部工具 ──

        /// <summary>文件名/名称安全校验（防穿越，F7/C6——对齐 Dashboard ResolveDashboardPath）：
        /// 非空 + 拒绝纯点组合 + 路径分隔符 + 盘符/冒号 + Path.GetFileName 归一不等（含 . / .. 穿越段）。</summary>
        private static void EnsureSafeName(string value, string paramName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);   // 先于防穿越（C6——GetFileName("")=="" 可绕过）
            if (value is "." or "..")
                throw new ArgumentException("名称不允许为纯点组合（. / ..）", paramName);
            if (value.Contains('/') || value.Contains('\\'))
                throw new ArgumentException("名称不允许包含路径分隔符（/ \\）", paramName);
            if (value.Contains(':'))
                throw new ArgumentException("名称不允许包含盘符/冒号（:）", paramName);
            if (Path.GetFileName(value) != value)
                throw new ArgumentException("名称非法（不得含路径分隔符/盘符/穿越段）", paramName);
        }

        /// <summary>文件名安全校验（防穿越超集——文件名会进入 Blob 存储 name 参数）。</summary>
        private static void EnsureSafeFileName(string value, string paramName)
            => EnsureSafeName(value, paramName);

        /// <summary>限长副本（C4 双路径）：seekable → Length 超限 fail-fast（不读流）；
        /// 非 seekable → 边复制边字节计数，超限立即中断（ArgumentException 含实际/限制值）。
        /// destination 由调用方 using 托管（异常时一并释放）。</summary>
        private static async Task CopyBoundedAsync(Stream source, MemoryStream destination, long maxBytes, CancellationToken ct)
        {
            if (source.CanSeek && source.Length > maxBytes)
                throw new ArgumentException($"文件大小 {source.Length} 字节超过限制 {maxBytes} 字节", nameof(source));

            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > maxBytes)
                    throw new ArgumentException($"文件超过大小限制 {maxBytes} 字节（实际已读 ≥ {total}）", nameof(source));
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }

        /// <summary>流式 SHA256（DataPort 先例——ComputeHashAsync + Hex 小写）。</summary>
        private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken ct)
        {
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>上传失败补偿：best-effort 删除已写 Blob（失败 → 日志警告，孤儿容忍）+ 不抛（保持原异常传播）。</summary>
        private async Task TryCleanupBlobAsync(string path, CancellationToken ct)
        {
            try
            {
                await _blobStorage.DeleteAsync(path, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "上传失败补偿清理 Blob 失败（孤儿容忍）：{Path}", path);
            }
        }

        /// <summary>递归把 childrenMap 中的子节点挂到 node 上（同级 SortOrder 升序）。</summary>
        private static FileFolderTreeNode AttachChildren(
            FileFolderTreeNode node,
            IReadOnlyDictionary<long, List<FileFolderTreeNode>> childrenMap)
        {
            if (!childrenMap.TryGetValue(node.Id, out var children) || children.Count == 0)
                return node;

            children.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            var resolved = new List<FileFolderTreeNode>(children.Count);
            foreach (var child in children)
                resolved.Add(AttachChildren(child, childrenMap));

            return node with { Children = resolved.AsReadOnly() };
        }

        /// <summary>
        /// 判定异常链是否含数据库唯一约束冲突（P3——上传/重命名/移动路径复用，对齐 OU IsUniqueConstraintViolation）。
        /// <para>SQLite：<see cref="Microsoft.Data.Sqlite.SqliteException.SqliteErrorCode"/> 19 = SQLITE_CONSTRAINT；
        /// 兜底消息特征（UNIQUE constraint failed / duplicate key / Duplicate entry）覆盖其他 Provider。</para>
        /// </summary>
        private static bool IsUniqueConstraintViolation(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
                    return true;

                var message = current.Message;
                if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
