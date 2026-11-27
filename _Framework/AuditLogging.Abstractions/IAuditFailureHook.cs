namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 同步落库失败回调（主通道，<b>恒触发、永不抛</b>）。
    /// <para>审计级信号不能只靠异步事件；本回调在 store 的 catch 内同步调用，保证不丢。
    /// 默认注册 no-op 实现；消费方 <c>TryAdd</c> 替换为告警/SIEM 联动。</para>
    /// </summary>
    public interface IAuditFailureHook
    {
        void OnPersistFailed(AuditPersistFailure failure);
    }
}
