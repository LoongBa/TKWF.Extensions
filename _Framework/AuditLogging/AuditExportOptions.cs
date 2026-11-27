namespace TKWF.Ext.AuditLogging
{
    /// <summary>审计导出配置（<c>TKWF:AuditLogging:Export</c>）。</summary>
    public class AuditExportOptions
    {
        /// <summary>单次导出最大行数（默认 10000；超限抛 <see cref="AuditExportLimitExceededException"/>）。</summary>
        public int MaxRows { get; set; } = 10_000;
    }
}
