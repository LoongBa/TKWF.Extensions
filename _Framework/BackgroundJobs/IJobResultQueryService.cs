using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 业务结果查询接口（V0.1.0）——按 JobId 查最新/分页。
/// </summary>
public interface IJobResultQueryService
{
    /// <summary>分页过滤查询（JobId/ResultType/时间范围）。</summary>
    Task<JobResultPagedResult> GetListAsync(JobResultQueryInput input, CancellationToken ct = default);

    /// <summary>按 JobId 查最新一条结果。</summary>
    Task<JobResultListItemDto?> GetLatestAsync(string jobId, CancellationToken ct = default);
}

/// <summary>
/// 业务结果查询输入（V0.1.0）。
/// </summary>
public sealed record JobResultQueryInput(
    string? JobId = null,
    string? ResultType = null,
    DateTime? StartFromUtc = null,
    DateTime? StartToUtc = null,
    int Skip = 0,
    int Take = 50);

/// <summary>
/// 业务结果分页结果（V0.1.0）。
/// </summary>
public sealed record JobResultPagedResult(
    int Total,
    int Skip,
    int Take,
    IReadOnlyList<JobResultListItemDto> Items);

/// <summary>
/// 业务结果列表项 DTO（V0.1.0）。
/// </summary>
public sealed record JobResultListItemDto
{
    public long Id { get; init; }
    public string JobId { get; init; } = "";
    public string ResultType { get; init; } = "success";
    public string? ResultJson { get; init; }
    public string? Summary { get; init; }
    public DateTime CreateTime { get; init; }
}
