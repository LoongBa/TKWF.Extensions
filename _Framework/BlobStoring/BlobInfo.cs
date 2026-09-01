namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// Blob 信息模型——定义上传成功后返回的元数据（名称、路径、内容类型、大小）。
    /// </summary>
    public class BlobInfo
    {
        /// <summary>Blob 名称（文件名）。</summary>
        public string Name { get; set; } = "";

        /// <summary>存储路径（相对 RootPath 的路径）。</summary>
        public string Path { get; set; } = "";

        /// <summary>MIME 内容类型。</summary>
        public string? ContentType { get; set; }

        /// <summary>文件大小（字节）。</summary>
        public long Size { get; set; }
    }
}
