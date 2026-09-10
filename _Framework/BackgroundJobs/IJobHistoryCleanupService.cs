using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 执行历史清理接口（V0.2.0）——按保留天数（<see cref="BackgroundJobsPersistenceOptions.RetentionDays"/>，默认 180）分批删除过期的
/// 执行历史（JobExecution，锚点 StartedAtUtc）与业务结果（JobResult，锚点 CreateTime）。
/// <para>扩展不内建调度器——由消费方经 BackgroundJob/Quartz/Hangfire 定时调用；单表失败 LogWarning 静默，不阻断另一表。</para>
/// </summary>
public interface IJobHistoryCleanupService
{
    /// <summary>清理过期历史——返回两表实际删除条数。</summary>
    Task<JobHistoryCleanupResult> CleanupAsync(CancellationToken ct = default);
}

/// <summary>
/// 历史清理结果（V0.2.0）——两表实际删除条数。
/// </summary>
public sealed record JobHistoryCleanupResult(
    int DeletedExecutions,
    int DeletedResults);