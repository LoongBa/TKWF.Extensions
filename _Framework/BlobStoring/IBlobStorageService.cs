using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// Blob 存储抽象——定义二进制大对象的上传/下载/删除/检查操作。
    /// <para>V0.1.0 提供本地文件系统默认实现（<see cref="LocalStorageService"/>）；
    /// 后续可扩展 Azure Blob / S3 / MinIO 等。</para>
    /// </summary>
    public interface IBlobStorageService
    {
        /// <summary>上传 Blob，返回元数据信息（失败返回 null——V0.1.1 评审修复：可区分成功/失败）。</summary>
        Task<BlobInfo?> UploadAsync(string name, Stream content, string? contentType = null, CancellationToken ct = default);

        /// <summary>下载 Blob，返回内容流（不存在返回 null）。</summary>
        Task<Stream?> DownloadAsync(string path, CancellationToken ct = default);

        /// <summary>删除 Blob，返回是否成功。</summary>
        Task<bool> DeleteAsync(string path, CancellationToken ct = default);

        /// <summary>检查 Blob 是否存在。</summary>
        Task<bool> ExistsAsync(string path, CancellationToken ct = default);
    }
}
