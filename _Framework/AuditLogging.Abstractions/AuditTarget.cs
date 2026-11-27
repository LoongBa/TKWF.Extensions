namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计目标（业务对象类型 + 标识）。例：<c>new AuditTarget("merchant", merchantId)</c>。
    /// </summary>
    public sealed record AuditTarget(string TargetType, string? TargetId = null);
}
