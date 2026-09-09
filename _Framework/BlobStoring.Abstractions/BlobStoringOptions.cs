// 契约抽象（ADR50 L2 依赖倒置）——BlobStoring 实现与 FileManagement 消费方共用
// 命名空间保持 TKWF.Ext.BlobStoring：既有消费方 using 不变，零破坏。
namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// 二进制存储配置选项。
    /// <para>契约定义于 <c>TKWF.Ext.BlobStoring.Abstractions</c>（ADR50 L2 依赖倒置）；
    /// BlobStoring 扩展初始化器经 <c>AddOptions&lt;BlobStoringOptions&gt;().BindConfiguration("TKWF:BlobStoring")</c> 绑定。</para>
    /// </summary>
    public class BlobStoringOptions
    {
        /// <summary>Blob 存储根目录（默认 "./blobs"）。</summary>
        public string RootPath { get; set; } = "./blobs";

        /// <summary>是否启用二进制存储（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;
    }
}
