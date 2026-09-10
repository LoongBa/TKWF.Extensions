namespace TKWF.Ext.FileManagement
{
    /// <summary>
    /// 文件管理配置选项。
    /// <para>通过 <c>services.AddOptions&lt;FileManagementOptions&gt;().BindConfiguration("TKWF:FileManagement")</c> 绑定。</para>
    /// </summary>
    public class FileManagementOptions
    {
        /// <summary>允许上传的扩展名白名单（小写含点，如 ".jpg"；AllowAnyExtension=false 时生效）。</summary>
        public string[] AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".gif", ".pdf", ".txt", ".csv", ".xlsx", ".docx", ".zip"];

        /// <summary>是否允许任意扩展名（true = 跳过白名单校验；默认 false）。</summary>
        public bool AllowAnyExtension { get; set; } = false;

        /// <summary>单文件大小上限（字节，默认 10MB）。</summary>
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>是否开启 SHA256 去重（同目录同名同内容幂等返回既有文件；默认 true）。</summary>
        public bool Deduplicate { get; set; } = true;

        /// <summary>查询分页默认页大小。</summary>
        public int DefaultPageSize { get; set; } = 50;

        // ── V0.2.0 配额（可空默认不限制——无配置消费方行为不变） ──

        /// <summary>目录文件数上限（null = 不限制；超限上传 → InvalidOperationException）。</summary>
        public int? MaxFilesPerFolder { get; set; }

        /// <summary>目录容量上限（字节；null = 不限制；超限上传 → InvalidOperationException）。</summary>
        public long? MaxFolderSizeBytes { get; set; }

        /// <summary>全局总容量上限（字节；null = 不限制；超限上传 → InvalidOperationException）。</summary>
        public long? MaxTotalSizeBytes { get; set; }
    }
}
