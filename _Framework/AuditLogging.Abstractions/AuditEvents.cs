using System;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>调用审计落库成功（本地事件，默认关）。</summary>
    public sealed record AuditLogPersisted(
        long Id,
        DateTimeOffset ExecutionTime,
        string? ServiceName,
        string? MethodName,
        bool Success,
        int DurationMs,
        string? CorrelationId);

    /// <summary>调用审计落库失败（本地事件，默认关；主通道为 <see cref="IAuditFailureHook"/>）。</summary>
    public sealed record AuditLogPersistFailed(
        string? ServiceName,
        string? MethodName,
        DateTimeOffset ExecutionTime,
        string ExceptionType,
        string? CorrelationId);

    /// <summary>安全事件落库成功（本地事件，默认关）。</summary>
    public sealed record SecurityLogPersisted(
        long Id,
        string EventType,
        string Result,
        string? IpAddress,
        string? UserName,
        string? CorrelationId);

    /// <summary>安全事件落库失败（本地事件，默认关；主通道为 <see cref="IAuditFailureHook"/>）。</summary>
    public sealed record SecurityLogPersistFailed(
        string EventType,
        DateTimeOffset ExecutionTime,
        string ExceptionType,
        string? CorrelationId);

    /// <summary>审计清理完成（同步返回值为权威 + 本地事件可选）。</summary>
    public sealed record AuditCleanupCompleted(
        AuditFacet Facet,
        int RetentionDays,
        long DeletedCount,
        int Rounds,
        TimeSpan Elapsed);
}
