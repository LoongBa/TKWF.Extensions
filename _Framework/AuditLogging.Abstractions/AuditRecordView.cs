using System;
using System.Collections.Generic;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计统一记录视图（读侧）。基类只含三面公共字段；面差异在子类型。
    /// <para><b>Attributes 仅在 <see cref="BusinessAuditView"/></b>（v0.7 仅业务面有扩展列，C12）。</para>
    /// </summary>
    public abstract record AuditRecordView(
        AuditFacet Facet,
        long Id,
        AuditActor? Actor,
        AuditTarget? Target,
        string? ActionType,
        string? Summary,
        string? CorrelationId,
        DateTimeOffset ExecutionTime);

    /// <summary>调用审计视图。</summary>
    public sealed record InvocationAuditView(
        long Id,
        AuditActor? Actor,
        AuditTarget? Target,
        string? ActionType,
        string? Summary,
        string? CorrelationId,
        DateTimeOffset ExecutionTime,
        string? ServiceName,
        string? MethodName,
        int DurationMs,
        bool Success)
        : AuditRecordView(AuditFacet.Invocation, Id, Actor, Target, ActionType, Summary, CorrelationId, ExecutionTime);

    /// <summary>安全事件视图。</summary>
    public sealed record SecurityAuditView(
        long Id,
        AuditActor? Actor,
        AuditTarget? Target,
        string? ActionType,
        string? Summary,
        string? CorrelationId,
        DateTimeOffset ExecutionTime,
        string EventType,
        string Result,
        string? IpAddress,
        string? UserName)
        : AuditRecordView(AuditFacet.Security, Id, Actor, Target, ActionType, Summary, CorrelationId, ExecutionTime);

    /// <summary>业务事实视图（含 Before/After/Attributes）。</summary>
    public sealed record BusinessAuditView(
        long Id,
        AuditActor? Actor,
        AuditTarget? Target,
        string? ActionType,
        string? Summary,
        string? CorrelationId,
        DateTimeOffset ExecutionTime,
        string? BeforeJson,
        string? AfterJson,
        IReadOnlyDictionary<string, object?>? Attributes)
        : AuditRecordView(AuditFacet.Business, Id, Actor, Target, ActionType, Summary, CorrelationId, ExecutionTime);
}
