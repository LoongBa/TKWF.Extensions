using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 业务结果查询服务（V0.1.0）——委托 <see cref="JobResultEntityDataService"/> 实现最新/分页查询。
/// <para>异常静默：查询失败返回空/默认值，不阻断消费方。</para>
/// </summary>
internal sealed class JobResultQueryService : IJobResultQueryService
{
    private readonly JobResultEntityDataService _dataService;
    private readonly ILogger<JobResultQueryService> _logger;

    public JobResultQueryService(JobResultEntityDataService dataService, ILogger<JobResultQueryService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>分页过滤查询（JobId/ResultType/时间范围）——Total 为过滤后总条数（SQL COUNT）。</summary>
    public async Task<JobResultPagedResult> GetListAsync(JobResultQueryInput input, CancellationToken ct = default)
    {
        try
        {
            var skip = Math.Max(0, input.Skip);
            var take = Math.Clamp(input.Take, 1, 200);

            var total = await _dataService.CountAsync(
                input.JobId, input.ResultType,
                input.StartFromUtc, input.StartToUtc, ct);

            var entities = await _dataService.GetListAsync(
                input.JobId, input.ResultType,
                input.StartFromUtc, input.StartToUtc,
                skip, take, ct);

            var items = entities.Select(e => new JobResultListItemDto
            {
                Id = e.Id,
                JobId = e.JobId,
                ResultType = e.ResultType,
                ResultJson = e.ResultJson,
                Summary = e.Summary,
                CreateTime = e.CreateTime
            }).ToList();

            return new JobResultPagedResult((int)total, skip, take, items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "业务结果分页查询失败");
            return new JobResultPagedResult(0, input.Skip, input.Take, []);
        }
    }

    /// <summary>按 JobId 查最新一条结果。</summary>
    public async Task<JobResultListItemDto?> GetLatestAsync(string jobId, CancellationToken ct = default)
    {
        try
        {
            var entity = await _dataService.GetLatestByJobIdAsync(jobId, ct);
            if (entity == null) return null;

            return new JobResultListItemDto
            {
                Id = entity.Id,
                JobId = entity.JobId,
                ResultType = entity.ResultType,
                ResultJson = entity.ResultJson,
                Summary = entity.Summary,
                CreateTime = entity.CreateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "业务结果最新查询失败: JobId={JobId}", jobId);
            return null;
        }
    }
}
