namespace TKWF.Ext.AuditLogging
{
    /// <summary>默认失败回调（no-op）——消费方 <c>TryAdd</c> 替换为告警/SIEM 联动。</summary>
    internal sealed class NullAuditFailureHook : IAuditFailureHook
    {
        public void OnPersistFailed(AuditPersistFailure failure)
        {
        }
    }
}
