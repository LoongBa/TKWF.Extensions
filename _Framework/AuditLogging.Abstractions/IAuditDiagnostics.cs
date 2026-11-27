namespace TKWF.Ext.AuditLogging
{
    /// <summary>审计诊断计数快照（只读）。</summary>
    public sealed record AuditDiagnosticsSnapshot(
        long WriteFailures,
        long QueryFailures,
        long CleanupFailures,
        long EventsPublished,
        long EventPublishFailures);

    /// <summary>
    /// 审计诊断计数器（对外只读快照）。实现侧在扩展写路径内部累加，消费方仅读取。
    /// </summary>
    public interface IAuditDiagnostics
    {
        AuditDiagnosticsSnapshot Snapshot();
    }
}
