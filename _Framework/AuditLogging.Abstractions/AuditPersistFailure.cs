using System;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 落库失败信号载荷（同步 hook）。<b>禁止携带 ArgumentsJson/Detail 原文</b>——只带标识与异常类型，
    /// 防止审计信号本身成为二次泄露通道。
    /// </summary>
    public sealed record AuditPersistFailure(
        AuditFacet Facet,
        DateTimeOffset OccurredAt,
        string? ServiceName,
        string? MethodName,
        string? CorrelationId,
        string ExceptionType);
}
