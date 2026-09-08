using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 执行历史查询服务（V0.1.0）——委托 <see cref="JobExecutionEntityDataService"/> 实现分页过滤/详情/SQL 级聚合统计。
/// <para>异常静默：查询失败返回空/默认值，不阻断消费方。</para>
/// </summary>
internal sealed class JobExecutionQueryService : IJobExecutionQueryService
{
    private readonly JobExecutionEntityDataService _dataService;
    private readonly ILogger<JobExecutionQueryService> _logger;

    public JobExecutionQueryService(JobExecutionEntityDataService dataService, ILogger<JobExecutionQueryService> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>分页过滤查询——Take 默认 50 上限 200（对齐 AuditLogging）；Total 为过滤后总条数（SQL COUNT）。</summary>
    public async Task<JobExecutionPagedResult> GetListAsync(JobExecutionQueryInput input, CancellationToken ct = default)
    {
        try
        {
            var skip = Math.Max(0, input.Skip);
            var take = Math.Clamp(input.Take, 1, 200);

            var total = await _dataService.CountAsync(
                input.JobId, input.JobType, input.Provider,
                input.IsSuccess, input.IsCancelled, input.TenantId,
                input.StartFromUtc, input.StartToUtc, ct);

            var entities = await _dataService.GetListAsync(
                input.JobId, input.JobType, input.Provider,
                input.IsSuccess, input.IsCancelled, input.TenantId,
                input.StartFromUtc, input.StartToUtc,
                skip, take, ct);

            // 列表 DTO 不含 ErrorText（安全决策）
            var items = entities.Select(e => new JobExecutionListItemDto
            {
                Id = e.Id,
                JobId = e.JobId,
                JobType = e.JobType,
                Provider = e.Provider,
                IsSuccess = e.IsSuccess,
                IsCancelled = e.IsCancelled,
                RetryAttempt = e.RetryAttempt,
                DurationMs = e.DurationMs,
                StartedAtUtc = e.StartedAtUtc,
                CompletedAtUtc = e.CompletedAtUtc,
                TenantId = e.TenantId,
                CreateTime = e.CreateTime
            }).ToList();

            return new JobExecutionPagedResult((int)total, skip, take, items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行历史分页查询失败");
            return new JobExecutionPagedResult(0, input.Skip, input.Take, []);
        }
    }

    /// <summary>按 Id 查详情（含 ErrorText）。</summary>
    public async Task<JobExecutionDetailDto?> GetDetailAsync(long id, CancellationToken ct = default)
    {
        try
        {
            var entity = await _dataService.GetEntityByIdAsync(id, ct);
            if (entity == null) return null;

            return new JobExecutionDetailDto
            {
                Id = entity.Id,
                JobId = entity.JobId,
                JobType = entity.JobType,
                Provider = entity.Provider,
                IsSuccess = entity.IsSuccess,
                IsCancelled = entity.IsCancelled,
                RetryAttempt = entity.RetryAttempt,
                DurationMs = entity.DurationMs,
                StartedAtUtc = entity.StartedAtUtc,
                CompletedAtUtc = entity.CompletedAtUtc,
                ErrorText = entity.ErrorText,
                TenantId = entity.TenantId,
                CreateTime = entity.CreateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行历史详情查询失败: Id={Id}", id);
            return null;
        }
    }

    /// <summary>SQL 级聚合统计（Oracle C1）——委托 DataService SQL COUNT/AVG/MAX。</summary>
    public async Task<JobExecutionStats> GetStatsAsync(TimeSpan? window = null, CancellationToken ct = default)
    {
        try
        {
            return await _dataService.GetStatsAsync(window, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行历史统计查询失败");
            return new JobExecutionStats(0, 0, 0, 0, 0, 0);
        }
    }
}
