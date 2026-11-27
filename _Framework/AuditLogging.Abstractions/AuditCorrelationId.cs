namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// CorrelationId 单源约束（三面同域；SecurityLog 列宽对齐本值 128）。
    /// </summary>
    public static class AuditCorrelationId
    {
        public const int MaxLength = 128;
    }
}
