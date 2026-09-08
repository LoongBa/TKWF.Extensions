using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.BackgroundJobs;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 业务结果记录器（V0.1.0）——<see cref="IJobResultRecorder"/> 实现，
/// JobId 从 <see cref="BackgroundJobContext.Current"/> 读取，委托 <see cref="JobResultEntityDataService"/> 落库。
/// <para>异常静默 + ILogger.Warning——作业内记录失败不阻断作业执行。</para>
/// </summary>
internal sealed class JobResultRecorder : IJobResultRecorder
{
    private readonly JobResultEntityDataService _dataService;
    private readonly ILogger<JobResultRecorder> _logger;

    public JobResultRecorder(JobResultEntityDataService dataService, ILogger<JobResultRecorder> logger)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>记录业务产出——JobId 从 BackgroundJobContext.Current 读取；无上下文抛 InvalidOperationException。</summary>
    public async Task<long> RecordAsync(string resultType, string resultJson, string? summary = null, CancellationToken ct = default)
    {
        var context = BackgroundJobContext.Current
            ?? throw new InvalidOperationException(
                "无法记录业务结果：当前没有 BackgroundJobContext（必须在后台作业执行上下文内调用 IJobResultRecorder.RecordAsync）");

        var entity = new JobResultEntity
        {
            JobId = context.JobId ?? "",
            ResultType = resultType ?? "success",
            ResultJson = resultJson,
            Summary = summary,
            CreateTime = DateTime.UtcNow
        };

        try
        {
            var result = await _dataService.EntityCreateAsync(entity, ct);
            return result.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "业务结果落库失败: JobId={JobId}, ResultType={ResultType}", context.JobId, resultType);
            return 0;
        }
    }
}
