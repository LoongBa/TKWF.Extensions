namespace TKWF.Ext.DataPort
{
    /// <summary>
    /// DataPort 扩展配置——绑定 TKWF:DataPort 配置节。
    /// </summary>
    public sealed class DataPortOptions
    {
        /// <summary>默认批次大小（每批处理行数，≥1）。</summary>
        public int DefaultBatchSize { get; set; } = 500;

        /// <summary>批次回调异常时是否终止（false = 继续下一批）。</summary>
        public bool StopOnBatchFailure { get; set; } = false;

        /// <summary>默认 Provider 名称（IImportProvider/IExportProvider 注册名）。</summary>
        public string DefaultProvider { get; set; } = "miniexcel";
    }
}