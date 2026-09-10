using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// 本地文件系统 Blob 存储实现——在指定根目录下读写文件。
    /// <para>异常静默处理：文件读写失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。
    /// 但用户可控路径（UploadAsync 的 name / DownloadAsync、DeleteAsync、ExistsAsync 的 path）
    /// 防穿越校验失败时抛 <see cref="ArgumentException"/>（fail-closed，C2 评审修复——不再静默吞掉/返回 null）。</para>
    /// </summary>
    internal sealed class LocalStorageService : IBlobStorageService
    {
        private readonly IOptions<BlobStoringOptions> _options;
        private readonly ILogger<LocalStorageService> _logger;

        public LocalStorageService(IOptions<BlobStoringOptions> options, ILogger<LocalStorageService> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<BlobInfo?> UploadAsync(string name, Stream content, string? contentType = null, CancellationToken ct = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            // C2 防穿越：name 是唯一完全由调用方控制的路径段——Path.Combine(Guid, name) 中 name="../../x" 可逃逸根目录。
            // 校验失败抛 ArgumentException（fail-closed，不再静默返回 null）。
            EnsureSafeFileName(name);

            try
            {
                var opts = _options.Value;
                var rootPath = opts.RootPath;
                EnsureDirectoryExists(rootPath);

                var relativePath = Path.Combine(Guid.NewGuid().ToString("N"), name);
                var fullPath = Path.Combine(rootPath, relativePath);
                EnsureDirectoryExists(Path.GetDirectoryName(fullPath)!);

                // 写入文件
                await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await content.CopyToAsync(fileStream, ct);
                await fileStream.FlushAsync(ct);

                var size = new FileInfo(fullPath).Length;

                return new BlobInfo
                {
                    Name = name,
                    Path = relativePath,
                    ContentType = contentType,
                    Size = size
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Blob 上传失败: Name={Name}", name);
                // V0.1.1（评审修复）：返回 null 而非空 BlobInfo——调用方可区分成功/失败（null=失败）
                return null!;
            }
        }

        /// <summary>
        /// V0.2.0：直接返回 <see cref="FileStream"/>（流式下载，大文件不占内存）——不再复制到 MemoryStream。
        /// <para>接口契约 <c>Task&lt;Stream?&gt;</c> 语义：返回的流由调用方负责 Dispose；不存在返回 <c>null</c>。</para>
        /// </summary>
        public Task<Stream?> DownloadAsync(string path, CancellationToken ct = default)
        {
            // C2 防穿越：path 为用户可控相对路径，校验失败抛 ArgumentException（安全缺陷 fail-closed）。
            // 合法路径不受影响——BlobStoring 内部生成的 path 形如 {guid}/{name}，可通过校验。
            EnsureSafeRelativePath(path, nameof(path));

            try
            {
                ct.ThrowIfCancellationRequested();
                var opts = _options.Value;
                var fullPath = Path.Combine(opts.RootPath, path);

                if (!File.Exists(fullPath))
                    return Task.FromResult<Stream?>(null);

                // V0.2.0：直接返回 FileStream（流式下载，大文件不占内存）——调用方负责 Dispose
                return Task.FromResult<Stream?>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Blob 下载失败: Path={Path}", path);
                return Task.FromResult<Stream?>(null);
            }
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            // C2 防穿越：校验失败抛 ArgumentException（安全缺陷 fail-closed）。
            EnsureSafeRelativePath(path, nameof(path));

            try
            {
                var opts = _options.Value;
                var fullPath = Path.Combine(opts.RootPath, path);

                if (!File.Exists(fullPath))
                    return Task.FromResult(false);

                File.Delete(fullPath);
                return Task.FromResult(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Blob 删除失败: Path={Path}", path);
                return Task.FromResult(false);
            }
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            // C2 防穿越：校验失败抛 ArgumentException（安全缺陷 fail-closed）。
            EnsureSafeRelativePath(path, nameof(path));

            try
            {
                var opts = _options.Value;
                var fullPath = Path.Combine(opts.RootPath, path);
                return Task.FromResult(File.Exists(fullPath));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Blob 存在性检查失败: Path={Path}", path);
                return Task.FromResult(false);
            }
        }

        /// <summary>确保目录存在（不存在则创建）。</summary>
        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        /// <summary>
        /// 相对路径防穿越校验（C2 评审修复，Download/Delete/Exists 共用）——
        /// 拒绝空/空白路径、"."/".." 段、盘符（':'）注入。
        /// <para>只用于用户可控输入路径；BlobStoring 内部生成的合法路径（"{guid}/{name}"）不受影响。
        /// 校验通过后仍保留 Guid 隔离逻辑（Guid 段防文件名冲突）。</para>
        /// </summary>
        private static void EnsureSafeRelativePath(string path, string paramName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var safe = path.Replace('\\', '/');
            var segments = safe.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
                throw new ArgumentException("路径包含非法段（. / ..）", paramName);
            if (segments.Any(s => s.Contains(':')))
                throw new ArgumentException("路径包含非法盘符", paramName);
        }

        /// <summary>
        /// 上传文件名校验（C2 评审修复，UploadAsync 专用）——name 须为单段文件名：
        /// 先过通用相对路径防穿越校验（拒绝 "."/".."/盘符），再拒绝分隔符注入（<see cref="Path.GetFileName(string)"/> 语义）。
        /// </summary>
        private static void EnsureSafeFileName(string name)
        {
            EnsureSafeRelativePath(name, nameof(name));
            if (Path.GetFileName(name) != name)
                throw new ArgumentException("名称包含路径分隔符注入", nameof(name));
        }
    }
}
