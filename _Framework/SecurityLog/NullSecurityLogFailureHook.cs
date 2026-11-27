using TKWF.Ext.AuditLogging;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>安全日志落库失败信号的默认 no-op 实现（消费方可 services.Replace 替换）。</summary>
    internal sealed class NullSecurityLogFailureHook : IAuditFailureHook
    {
        public void OnPersistFailed(AuditPersistFailure failure)
        {
        }
    }
}
