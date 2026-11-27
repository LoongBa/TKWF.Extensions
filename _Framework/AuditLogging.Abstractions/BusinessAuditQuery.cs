using System;
using System.Collections.Generic;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>业务审计查询输入（过滤字段均可选；分页默认 50）。</summary>
    public sealed record BusinessAuditQueryInput
    {
        public string? TenantId { get; init; }
        public string? TargetType { get; init; }
        public string? TargetId { get; init; }
        public string? ActionType { get; init; }
        public string? ActorType { get; init; }
        public string? ActorId { get; init; }
        public string? CorrelationId { get; init; }
        public DateTimeOffset? FromUtc { get; init; }
        public DateTimeOffset? ToUtc { get; init; }

        /// <summary>keyset 游标：仅返回 Id &gt; AfterId（Id 升序决胜），供导出/深分页。</summary>
        public long? AfterId { get; init; }

        public int Skip { get; init; }
        public int Take { get; init; } = 50;
    }

    /// <summary>业务审计列表项 DTO（<b>不含 Before/After 全文</b>——防 PII/大字段）。</summary>
    public sealed record BusinessAuditListItemDto(
        long Id,
        string? TenantId,
        string ActorType,
        string? ActorId,
        string? ActorName,
        string TargetType,
        string? TargetId,
        string ActionType,
        string? Summary,
        string? CorrelationId,
        DateTimeOffset ExecutionTime,
        DateTimeOffset CreateTime);

    /// <summary>业务审计分页结果。</summary>
    public sealed record BusinessAuditPagedResult(
        long Total,
        IReadOnlyList<BusinessAuditListItemDto> Items);

    /// <summary>业务审计详情（<b>受控</b>：含 Before/After/AttributesJson，需 <see cref="AuditOperation.Detail"/> 放行；写入时已脱敏）。</summary>
    public sealed record BusinessAuditDetailDto(
        long Id,
        string? TenantId,
        string ActorType,
        string? ActorId,
        string? ActorName,
        string TargetType,
        string? TargetId,
        string ActionType,
        string? Summary,
        string? BeforeJson,
        string? AfterJson,
        string? AttributesJson,
        string? CorrelationId,
        DateTimeOffset ExecutionTime,
        DateTimeOffset CreateTime);
}
