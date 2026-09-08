using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.BackgroundJobs;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 作业执行监听器（V0.1.0）——<see cref="IBackgroundJobExecutionListener"/> 实现，
/// Context→实体映射→注入 <see cref="JobExecutionEntityDataService"/> 落库。
/// <para>异常静默 + ILogger.Warning——对齐契约语义：监听器异常绝不遮蔽业务异常、不阻断状态更新。</para>
/// <para>注册方式：<c>TryAddEnumerable(ServiceDescriptor.Scoped&lt;IBackgroundJobExecutionListener, JobExecutionRecorder&gt;())</c>，
/// 多监听器可叠加（Oracle C3，与契约一致）。</para>
/// </summary>
internal sealed class JobExecutionRecorder : IBackgroundJobExecutionListener
{
    private readonly JobExecutionEntityDataService _dataService;
    private readonly ILogger<JobExecutionRecorder> _logger;

    public JobExecutionRecorder(JobExecutionEntityDataService dataService, ILogger<JobExecutionRecorder> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>执行完成回调——Context→实体映射→DataService 落库（异常静默）。</summary>
    public async Task OnExecutedAsync(BackgroundJobExecutedContext context, CancellationToken ct = default)
    {
        try
        {
            var entity = new JobExecutionEntity
            {
                JobId = context.JobId ?? "",
                JobType = context.JobType ?? "",
                Provider = context.Provider ?? "",
                IsSuccess = context.IsSuccess,
                IsCancelled = context.IsCancelled,
                RetryAttempt = context.RetryAttempt,
                DurationMs = (long)context.Duration.TotalMilliseconds,
                StartedAtUtc = context.StartedAtUtc,
                CompletedAtUtc = context.CompletedAtUtc,
                ErrorText = context.Error,
                TenantId = context.TenantId,
                CreateTime = DateTime.UtcNow
            };

            await _dataService.EntityCreateAsync(entity, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "作业执行历史落库失败: JobId={JobId}, Provider={Provider}", context.JobId, context.Provider);
        }
    }
}
