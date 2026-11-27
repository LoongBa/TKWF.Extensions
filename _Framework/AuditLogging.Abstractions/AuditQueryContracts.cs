using System;
using System.Collections.Generic;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计分面查询（读侧统一查询参数）。<b>keyset 游标</b> <see cref="AfterId"/> 供导出/水位/深分页（C5）；
    /// <see cref="TenantId"/> 预留（C11，面级租户不对称见文档）。
    /// </summary>
    public sealed record AuditFacetQuery
    {
        public string? TenantId { get; init; }
        public string? TargetType { get; init; }
        public string? TargetId { get; init; }
        public string? ActionType { get; init; }
        public string? ActorType { get; init; }
        public string? ActorId { get; init; }
        public string? CorrelationId { get; init; }
        public string? EventType { get; init; }
        public string? Result { get; init; }
        public DateTimeOffset? FromUtc { get; init; }
        public DateTimeOffset? ToUtc { get; init; }

        /// <summary>keyset 游标：仅返回 Id &gt; AfterId 的记录（Id 升序决胜）。null = 从头。</summary>
        public long? AfterId { get; init; }

        public int Skip { get; init; }

        /// <summary>页大小（默认 50，上限由各面 Reader 钳制）。</summary>
        public int Take { get; init; } = 50;
    }

    /// <summary>分面分页结果。</summary>
    public sealed record AuditFacetPageResult(long Total, IReadOnlyList<AuditRecordView> Items);

    /// <summary>
    /// 跨面按 CorrelationId 聚合结果——<b>仅聚合已注册/已启用的面</b>，并显式给出参与面（C1，禁静默）。
    /// </summary>
    public sealed record AuditCrossFacetResult(
        IReadOnlyList<AuditFacet> ParticipatingFacets,
        IReadOnlyList<AuditRecordView> Items,
        bool Truncated = false);
}
