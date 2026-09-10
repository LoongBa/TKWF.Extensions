using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 执行历史清理服务（V0.2.0）——<see cref="IJobHistoryCleanupService"/> 实现。
/// <para>分表清理（JobExecution 锚点 StartedAtUtc / JobResult 锚点 CreateTime）：每轮 DeleteExpiredAsync 取 batchSize 条删之，
/// 循环直至不足一批（或已删除 0 条）；<see cref="MaxRounds"/> 上限保护防死循环。</para>
/// <para>异常静默（对齐扩展既有模式）：任一表清理失败 LogWarning，不阻断另一表清理。</para>
/// <para>列注入：两个 SG1 DataService + <see cref="IOptions{BackgroundJobsPersistenceOptions}"/> + Logger——
/// 数据访问全部经 DataService（红线合规），不触碰 ORM/IEntityDAC。</para>
/// </summary>
internal sealed class JobHistoryCleanupService : IJobHistoryCleanupService
{
    private const int MaxRounds = 100;

    private readonly JobExecutionEntityDataService _executionDataService;
    private readonly JobResultEntityDataService _resultDataService;
    private readonly IOptions<BackgroundJobsPersistenceOptions> _options;
    private readonly ILogger<JobHistoryCleanupService> _logger;

    public JobHistoryCleanupService(
        JobExecutionEntityDataService executionDataService,
        JobResultEntityDataService resultDataService,
        IOptions<BackgroundJobsPersistenceOptions> options,
        ILogger<JobHistoryCleanupService> logger)
    {
        _executionDataService = executionDataService ?? throw new ArgumentNullException(nameof(executionDataService));
        _resultDataService = resultDataService ?? throw new ArgumentNullException(nameof(resultDataService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>清理过期历史——分表分批循环直至清空，两表独立容错。</summary>
    public async Task<JobHistoryCleanupResult> CleanupAsync(CancellationToken ct = default)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-_options.Value.RetentionDays);
        var batchSize = Math.Max(1, _options.Value.CleanupBatchSize);

        var deletedExecutions = await CleanupTableAsync(
            "JobExecution", cutoffUtc, batchSize, _executionDataService.DeleteExpiredAsync, ct);
        var deletedResults = await CleanupTableAsync(
            "JobResult", cutoffUtc, batchSize, _resultDataService.DeleteExpiredAsync, ct);

        return new JobHistoryCleanupResult(deletedExecutions, deletedResults);
    }

    /// <summary>单表分批清理——每轮删除 batchSize 条，直到返回少于一批或触达轮数上限；失败 LogWarning 返回已删条数。</summary>
    private async Task<int> CleanupTableAsync(
        string tableName, DateTime cutoffUtc, int batchSize,
        Func<DateTime, int, CancellationToken, Task<int>> deleteBatch, CancellationToken ct)
    {
        var total = 0;
        try
        {
            for (int round = 0; round < MaxRounds; round++)
            {
                ct.ThrowIfCancellationRequested();

                var deleted = await deleteBatch(cutoffUtc, batchSize, ct);
                total += deleted;
                if (deleted < batchSize)
                    break;
            }
        }
        catch (Exception ex)
        {
            // 异常静默（对齐扩展既有模式）：单表清理失败不阻断另一表
            _logger.LogWarning(ex, "{Table} 过期历史清理失败（异常静默）：已清理 {Deleted} 条", tableName, total);
        }
        return total;
    }
}