using System.Threading;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>审计诊断计数器（线程安全；对外经 <see cref="IAuditDiagnostics"/> 只读快照）。</summary>
    internal sealed class AuditDiagnostics : IAuditDiagnostics
    {
        private long _writeFailures;
        private long _queryFailures;
        private long _cleanupFailures;
        private long _eventsPublished;
        private long _eventPublishFailures;

        public void IncrementWriteFailures() => Interlocked.Increment(ref _writeFailures);

        public void IncrementQueryFailures() => Interlocked.Increment(ref _queryFailures);

        public void IncrementCleanupFailures() => Interlocked.Increment(ref _cleanupFailures);

        public void IncrementEventsPublished() => Interlocked.Increment(ref _eventsPublished);

        public void IncrementEventPublishFailures() => Interlocked.Increment(ref _eventPublishFailures);

        public AuditDiagnosticsSnapshot Snapshot() => new(
            Interlocked.Read(ref _writeFailures),
            Interlocked.Read(ref _queryFailures),
            Interlocked.Read(ref _cleanupFailures),
            Interlocked.Read(ref _eventsPublished),
            Interlocked.Read(ref _eventPublishFailures));
    }
}
