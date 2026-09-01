namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// 二进制存储配置选项。
    /// <para>通过 <c>services.Configure&lt;BlobStoringOptions&gt;(config.GetSection("TKWF:BlobStoring"))</c> 绑定。</para>
    /// </summary>
    public class BlobStoringOptions
    {
        /// <summary>Blob 存储根目录（默认 "./blobs"）。</summary>
        public string RootPath { get; set; } = "./blobs";

        /// <summary>是否启用二进制存储（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;
    }
}
