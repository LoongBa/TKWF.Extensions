namespace TKWF.Ext.AuditLogging
{
    /// <summary>默认 Actor 派生器（返回 null → 强制调用方显式传入 Actor）。</summary>
    internal sealed class NullAuditActorProvider : IAuditActorProvider
    {
        public AuditActor? ResolveActor() => null;
    }
}
