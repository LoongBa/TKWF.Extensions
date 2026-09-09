// 契约抽象（ADR50 L2 依赖倒置）——BlobStoring 实现与 FileManagement 消费方共用
// 命名空间保持 TKWF.Ext.BlobStoring：既有消费方 using 不变，零破坏。
namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// Blob 信息模型——定义上传成功后返回的元数据（名称、路径、内容类型、大小）。
    /// <para>契约定义于 <c>TKWF.Ext.BlobStoring.Abstractions</c>（ADR50 L2 依赖倒置）。</para>
    /// </summary>
    public class BlobInfo
    {
        /// <summary>Blob 名称（文件名）。</summary>
        public string Name { get; set; } = "";

        /// <summary>存储路径（相对 RootPath 的路径，形如 {guid}/{name}）。</summary>
        public string Path { get; set; } = "";

        /// <summary>MIME 内容类型。</summary>
        public string? ContentType { get; set; }

        /// <summary>文件大小（字节）。</summary>
        public long Size { get; set; }
    }
}
