using System.IO;
using System.Threading;
using System.Threading.Tasks;

// 契约抽象（ADR50 L2 依赖倒置）——BlobStoring 实现与 FileManagement 消费方共用
// 命名空间保持 TKWF.Ext.BlobStoring：既有消费方 using 不变，零破坏（对齐 Account.Abstractions Oracle P2-1c 迁移先例）。
namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// Blob 存储抽象——定义二进制大对象的上传/下载/删除/检查操作。
    /// <para>契约定义于 <c>TKWF.Ext.BlobStoring.Abstractions</c>（ADR50 L2 依赖倒置）——
    /// BlobStoring 实现项目与 FileManagement 等消费方共用；默认实现为本地文件系统
    /// <c>LocalStorageService</c>，后续可扩展 Azure Blob / S3 / MinIO 等。</para>
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
