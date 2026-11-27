namespace TKWF.Ext.AuditLogging
{
    /// <summary>审计面标识（统一读模型用）。</summary>
    public enum AuditFacet
    {
        /// <summary>调用审计（AuditLogging）。</summary>
        Invocation = 0,

        /// <summary>安全事件审计（SecurityLog）。</summary>
        Security = 1,

        /// <summary>业务事实审计（BusinessAudit）。</summary>
        Business = 2,
    }
}
