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

        /// <summary>Processing 超时（分钟）——启动/周期扫描时，超过该时长仍为 Processing 的记录重置为 Failed（默认 60；0 = 禁用恢复扫描）。</summary>
        public int ProcessingTimeoutMinutes { get; set; } = 60;

        /// <summary>导入心跳间隔（分钟）——导入执行中每批检查，距上次心跳达到该值即更新 UpdateTime（WHERE Status='Processing' 条件更新）；0 = 禁用心跳（默认 1）。</summary>
        public int HeartbeatIntervalMinutes { get; set; } = 1;
    }
}