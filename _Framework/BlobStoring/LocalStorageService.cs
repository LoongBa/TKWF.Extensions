using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// 本地文件系统 Blob 存储实现——在指定根目录下读写文件。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。
    /// 与 <see cref="FreeSqlBlobRecordStore"/> 模式一致。</para>
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
        public async Task<BlobInfo> UploadAsync(string name, Stream content, string? contentType = null, CancellationToken ct = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 上传失败: Name={Name}", name);
                // 返回空信息，不抛出异常
                return new BlobInfo { Name = name, Path = "", ContentType = contentType, Size = 0 };
            }
        }

        /// <inheritdoc />
        public async Task<Stream?> DownloadAsync(string path, CancellationToken ct = default)
        {
            try
            {
                var opts = _options.Value;
                var fullPath = Path.Combine(opts.RootPath, path);

                if (!File.Exists(fullPath))
                    return null;

                // 复制到 MemoryStream 以便返回（文件流可能被关闭）
                var memoryStream = new MemoryStream();
                await using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await fileStream.CopyToAsync(memoryStream, ct);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 下载失败: Path={Path}", path);
                return null;
            }
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
        {
            try
            {
                var opts = _options.Value;
                var fullPath = Path.Combine(opts.RootPath, path);

                if (!File.Exists(fullPath))
                    return Task.FromResult(false);

                File.Delete(fullPath);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 删除失败: Path={Path}", path);
                return Task.FromResult(false);
            }
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            try
            {
                var opts = _options.Value;
                var fullPath = Path.Combine(opts.RootPath, path);
                return Task.FromResult(File.Exists(fullPath));
            }
            catch (Exception ex)
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
    }
}
