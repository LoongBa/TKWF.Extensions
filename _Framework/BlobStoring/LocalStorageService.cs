using System;
using System.IO;
using System.Text.RegularExpressions;
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
    /// 防穿越校验失败时抛 <see cref="ArgumentException"/>（fail-closed，C2 评审修复——不再静默吞掉/返回 null）。
    /// V0.3.1（BL-P0 修复）：校验升级为段级白名单字符集（见 <see cref="SafeRelativePathPattern"/>）+ 规范化前缀双保险。</para>
    /// </summary>
    internal sealed class LocalStorageService : IBlobStorageService
    {
        private readonly IOptions<BlobStoringOptions> _options;
        private readonly ILogger<LocalStorageService> _logger;

        /// <summary>
        /// 相对路径段级白名单整路径锚定正则（V0.3.1 BL-P0）——段首与段尾必须字母数字，
        /// 中缀仅允许 <c>.</c>/<c>_</c>/<c>-</c>（文件名点号扩展），段间以 <c>/</c> 分隔；
        /// <c>\z</c> 严格字符串末尾（尾随换行必败）。白名单 = 不可包含也无需列举
        /// （前导分隔符/盘符/穿越段/控制字符等一切字符集外形态结构必败）。
        /// 与 Metrics M-01 / Dashboard D-01 逐段白名单先例同型（整体锚定实现）。
        /// </summary>
        private static readonly Regex SafeRelativePathPattern = new(
            @"^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?(?:/[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?)*\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public LocalStorageService(IOptions<BlobStoringOptions> options, ILogger<LocalStorageService> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<BlobInfo?> UploadAsync(string name, Stream content, string? contentType = null, CancellationToken ct = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            // C2/V0.3.1 防穿越：name 是唯一完全由调用方控制的路径段——Path.Combine(Guid, name) 中 name="../../x" 可逃逸根目录。
            // V0.3.1 起校验升级为段级白名单整路径锚定 + 规范化前缀双保险（BL-P0 rooted/UNC 闭环）。
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
            // C2/V0.3.1 防穿越：path 为用户可控相对路径——V0.3.1 起白名单整路径锚定 + 规范化前缀双保险
            // （拒绝绝对路径/UNC/盘符/尾点/尾分隔符/尾随换行等），校验失败抛 ArgumentException（安全缺陷 fail-closed）。
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
            // C2/V0.3.1 防穿越（白名单 + 规范化前缀双保险）：校验失败抛 ArgumentException（安全缺陷 fail-closed）。
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
            // C2/V0.3.1 防穿越（白名单 + 规范化前缀双保险）：校验失败抛 ArgumentException（安全缺陷 fail-closed）。
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
        /// 规范化前缀双保险第二层（V0.3.1 BL-P0）——<paramref name="fullPath"/> 必须等于 RootPath
        /// 或以 "RootPath + 目录分隔符" 开头（OrdinalIgnoreCase）；分隔符边界防兄弟目录前缀误判（如 root2）。
        /// </summary>
        private bool IsUnderRoot(string fullPath)
        {
            var root = Path.GetFullPath(_options.Value.RootPath);
            var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
                ? root : root + Path.DirectorySeparatorChar;
            return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 相对路径防穿越校验（V0.3.1 BL-P0 修复——段级白名单字符集 + 规范化前缀双保险，对齐 Metrics/Dashboard 白名单先例）：
        /// ① <see cref="SafeRelativePathPattern"/> 整路径锚定——拒绝绝对路径/UNC（前导分隔符）、"."/".." 段、
        ///    盘符/ADS（':'）、控制字符、空格、尾点/尾分隔符/尾随换行及一切白名单外字符；
        /// ② <see cref="Path.GetFullPath(string)"/> 规范化后必须位于 RootPath 之下（OrdinalIgnoreCase 前缀，<see cref="IsUnderRoot"/>）——
        ///    纵深防御（正则误配/未来放宽的兜底）。
        /// <para>只用于用户可控输入路径；BlobStoring 内部生成的合法路径（"{guid}/{name}"）不受影响。
        /// 校验失败抛 <see cref="ArgumentException"/>（fail-closed，延续 C2 语义）。</para>
        /// </summary>
        private void EnsureSafeRelativePath(string path, string paramName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            var normalized = path.Replace('\\', '/');
            // 整路径锚定正则（拒绝空段/尾分隔符/尾随换行——RemoveEmptyEntries 在此是陷阱：前导分隔符会被静默吞掉）
            if (!SafeRelativePathPattern.IsMatch(normalized))
                throw new ArgumentException(
                    "路径仅允许 段(首尾字母/数字、中缀./_-) 以 '/' 分隔（拒绝绝对路径/UNC/盘符/穿越段/尾点/尾随换行/空白及控制字符）", paramName);

            // 双保险：规范化后必须位于 RootPath 之下
            var full = Path.GetFullPath(Path.Combine(_options.Value.RootPath, normalized));
            if (!IsUnderRoot(full))
                throw new ArgumentException("路径解析后超出存储根目录", paramName);
        }

        /// <summary>
        /// 上传文件名校验（V0.3.1 起依赖白名单 <see cref="EnsureSafeRelativePath"/>，UploadAsync 专用）——
        /// name 须为单段文件名：先过通用相对路径防穿越校验（拒绝白名单外一切形态），
        /// 再拒绝分隔符注入（<see cref="Path.GetFileName(string)"/> 语义，防御纵深 + 精确错误消息）。
        /// </summary>
        private void EnsureSafeFileName(string name)
        {
            EnsureSafeRelativePath(name, nameof(name));
            if (Path.GetFileName(name) != name)
                throw new ArgumentException("名称包含路径分隔符注入", nameof(name));
        }
    }
}
