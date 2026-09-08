using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 执行历史查询接口（V0.1.0）——分页过滤 + SQL 级聚合统计 + GetDetailAsync。
/// <para>列表 DTO 不含 ErrorText（安全决策，对齐 AuditLogging 先例）；详情按 Id 取全量。</para>
/// </summary>
public interface IJobExecutionQueryService
{
    /// <summary>分页过滤查询（JobId/JobType/Provider/IsSuccess/IsCancelled/TenantId/时间范围）。</summary>
    Task<JobExecutionPagedResult> GetListAsync(JobExecutionQueryInput input, CancellationToken ct = default);

    /// <summary>按 Id 查详情（含 ErrorText）。</summary>
    Task<JobExecutionDetailDto?> GetDetailAsync(long id, CancellationToken ct = default);

    /// <summary>SQL 级聚合统计（Total/Succeeded/Failed/Cancelled/AvgDurationMs/MaxDurationMs）。</summary>
    Task<JobExecutionStats> GetStatsAsync(TimeSpan? window = null, CancellationToken ct = default);
}

/// <summary>
/// 执行历史查询输入（V0.1.0）——过滤条件 + 分页参数。
/// </summary>
public sealed record JobExecutionQueryInput(
    string? JobId = null,
    string? JobType = null,
    string? Provider = null,
    bool? IsSuccess = null,
    bool? IsCancelled = null,
    long? TenantId = null,
    DateTime? StartFromUtc = null,
    DateTime? StartToUtc = null,
    int Skip = 0,
    int Take = 50);

/// <summary>
/// 执行历史分页结果（V0.1.0）。
/// </summary>
public sealed record JobExecutionPagedResult(
    int Total,
    int Skip,
    int Take,
    IReadOnlyList<JobExecutionListItemDto> Items);

/// <summary>
/// 执行历史列表项 DTO（V0.1.0）——不含 ErrorText（安全决策，对齐 AuditLogging 先例）。
/// </summary>
public sealed record JobExecutionListItemDto
{
    public long Id { get; init; }
    public string JobId { get; init; } = "";
    public string JobType { get; init; } = "";
    public string Provider { get; init; } = "";
    public bool IsSuccess { get; init; }
    public bool IsCancelled { get; init; }
    public int RetryAttempt { get; init; }
    public long DurationMs { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }
    public long? TenantId { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>
/// 执行历史详情 DTO（V0.1.0）——含 ErrorText（按 Id 取全量）。
/// </summary>
public sealed record JobExecutionDetailDto
{
    public long Id { get; init; }
    public string JobId { get; init; } = "";
    public string JobType { get; init; } = "";
    public string Provider { get; init; } = "";
    public bool IsSuccess { get; init; }
    public bool IsCancelled { get; init; }
    public int RetryAttempt { get; init; }
    public long DurationMs { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime CompletedAtUtc { get; init; }
    public string? ErrorText { get; init; }
    public long? TenantId { get; init; }
    public DateTime CreateTime { get; init; }
}

/// <summary>
/// 执行历史统计结果（V0.1.0）——SQL 级聚合（Oracle C1）。
/// </summary>
public sealed record JobExecutionStats(
    int Total,
    int Succeeded,
    int Failed,
    int Cancelled,
    long AvgDurationMs,
    long MaxDurationMs);
