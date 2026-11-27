using System;
using System.Collections.Generic;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 业务事实审计条目——一行 = 一次业务对象变更事实（不是一次方法调用）。
    /// <para><see cref="Actor"/> 必填；<see cref="Summary"/>/<see cref="Before"/>/<see cref="After"/>
    /// 由写入管线按 <see cref="IAuditMaskingPolicy"/> 脱敏后序列化落库。</para>
    /// </summary>
    public sealed record BusinessAuditEntry(
        AuditActor Actor,
        AuditTarget Target,
        string ActionType,
        DateTimeOffset ExecutionTime,
        string? TenantId = null,
        string? Summary = null,
        object? Before = null,
        object? After = null,
        string? CorrelationId = null,
        IReadOnlyDictionary<string, object?>? Attributes = null);
}
