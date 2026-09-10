using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.BackgroundJobs;

namespace TKWF.Ext.BackgroundJobs.Tests;

/// <summary>
/// BackgroundJobs V0.2.0 历史清理测试——IJobHistoryCleanupService 验收覆盖：
/// 过期执行/结果删除 + 未过期保留 + RetentionDays 配置生效 + 无过期返回零 + 分批循环 + 单表失败静默不阻断另一表 + Options 默认值。
/// <para>真实内存 SQLite（对齐 BackgroundJobsTests.CreateHost 模式）；断言容错——
/// SQLite 读出 DateTime 可能 Unspecified/偏移，比较 Id 集合而非精确时间。</para>
/// </summary>
public class JobHistoryCleanupTests
{
    /// <summary>创建 SQLite :memory: + 建表 + DataService 链（对齐 BackgroundJobsTests.CreateHost）。</summary>
    private static (IFreeSql fsql, JobExecutionEntityDataService execDs, JobResultEntityDataService resultDs) CreateHost()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<JobExecutionEntity>();
        fsql.CodeFirst.SyncStructure<JobResultEntity>();

        var execDs = new JobExecutionEntityDataService(new StubDomainUser(),
            new FreeSqlEntityDAC<JobExecutionEntity>(new UnitOfWorkManager(fsql)));
        var resultDs = new JobResultEntityDataService(new StubDomainUser(),
            new FreeSqlEntityDAC<JobResultEntity>(new UnitOfWorkManager(fsql)));
        return (fsql, execDs, resultDs);
    }

    /// <summary>构造清理服务（默认 Options：RetentionDays=180，CleanupBatchSize=500）。</summary>
    private static JobHistoryCleanupService CreateService(
        JobExecutionEntityDataService execDs, JobResultEntityDataService resultDs,
        BackgroundJobsPersistenceOptions? options = null, ILogger<JobHistoryCleanupService>? logger = null)
        => new(execDs, resultDs, Options.Create(options ?? new BackgroundJobsPersistenceOptions()),
            logger ?? NullLogger<JobHistoryCleanupService>.Instance);

    /// <summary>构造 JobExecution 测试实体（StartedAtUtc 与 CreateTime 对齐锚点）。</summary>
    private static JobExecutionEntity CreateExecution(string jobId, DateTime startedAtUtc)
        => new()
        {
            JobId = jobId,
            JobType = "TestJob, TestAssembly",
            Provider = "builtin",
            IsSuccess = true,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = startedAtUtc.AddSeconds(1),
            CreateTime = startedAtUtc
        };

    /// <summary>构造 JobResult 测试实体（CreateTime 即清理锚点）。</summary>
    private static JobResultEntity CreateResult(string jobId, DateTime createTime)
        => new() { JobId = jobId, ResultType = "success", ResultJson = "{}", CreateTime = createTime };

    /// <summary>全部 JobExecution Id 集合（断言容错用）。</summary>
    private static async Task<List<long>> GetAllExecutionIdsAsync(JobExecutionEntityDataService execDs)
    {
        var rows = await execDs.EntitySelectAsync(e => true, 0, 10_000);
        return rows.Select(e => e.Id).ToList();
    }

    /// <summary>全部 JobResult Id 集合（断言容错用）。</summary>
    private static async Task<List<long>> GetAllResultIdsAsync(JobResultEntityDataService resultDs)
    {
        var rows = await resultDs.EntitySelectAsync(e => true, 0, 10_000);
        return rows.Select(e => e.Id).ToList();
    }

    // ═══════════════════════════════════════════════════════
    // V0.2.0: 历史清理
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task CleanupAsync_DeletesExpiredExecutions()
    {
        var (_, execDs, resultDs) = CreateHost();
        var expired = await execDs.EntityCreateAsync(CreateExecution("expired-1", DateTime.UtcNow.AddDays(-200)));
        await execDs.EntityCreateAsync(CreateExecution("expired-2", DateTime.UtcNow.AddDays(-365)));
        var service = CreateService(execDs, resultDs);

        var result = await service.CleanupAsync();

        Assert.Equal(2, result.DeletedExecutions);
        Assert.Equal(0, result.DeletedResults);
        Assert.DoesNotContain(expired.Id, await GetAllExecutionIdsAsync(execDs));
    }

    [Fact]
    public async Task CleanupAsync_KeepsRecentExecutions()
    {
        var (_, execDs, resultDs) = CreateHost();
        var expired = await execDs.EntityCreateAsync(CreateExecution("expired", DateTime.UtcNow.AddDays(-200)));
        var recent = await execDs.EntityCreateAsync(CreateExecution("recent", DateTime.UtcNow.AddDays(-1)));
        var service = CreateService(execDs, resultDs);

        var result = await service.CleanupAsync();

        Assert.Equal(1, result.DeletedExecutions);
        var remaining = await GetAllExecutionIdsAsync(execDs);
        Assert.DoesNotContain(expired.Id, remaining);
        Assert.Contains(recent.Id, remaining);
    }

    [Fact]
    public async Task CleanupAsync_DeletesExpiredResults()
    {
        var (_, execDs, resultDs) = CreateHost();
        var expired = await resultDs.EntityCreateAsync(CreateResult("r-expired-1", DateTime.UtcNow.AddDays(-200)));
        await resultDs.EntityCreateAsync(CreateResult("r-expired-2", DateTime.UtcNow.AddDays(-900)));
        var service = CreateService(execDs, resultDs);

        var result = await service.CleanupAsync();

        Assert.Equal(0, result.DeletedExecutions);
        Assert.Equal(2, result.DeletedResults);
        Assert.DoesNotContain(expired.Id, await GetAllResultIdsAsync(resultDs));
    }

    [Fact]
    public async Task CleanupAsync_RetentionDaysFromOptions()
    {
        var (_, execDs, resultDs) = CreateHost();
        // RetentionDays=5 → 仅清 5 天前的记录
        var old = await execDs.EntityCreateAsync(CreateExecution("old-10d", DateTime.UtcNow.AddDays(-10)));
        var recent = await execDs.EntityCreateAsync(CreateExecution("recent-2d", DateTime.UtcNow.AddDays(-2)));
        var service = CreateService(execDs, resultDs, new BackgroundJobsPersistenceOptions { RetentionDays = 5 });

        var result = await service.CleanupAsync();

        Assert.Equal(1, result.DeletedExecutions);
        var remaining = await GetAllExecutionIdsAsync(execDs);
        Assert.DoesNotContain(old.Id, remaining);
        Assert.Contains(recent.Id, remaining);
    }

    [Fact]
    public async Task CleanupAsync_NoExpired_ReturnsZero()
    {
        var (_, execDs, resultDs) = CreateHost();
        await execDs.EntityCreateAsync(CreateExecution("fresh-1", DateTime.UtcNow.AddDays(-1)));
        await resultDs.EntityCreateAsync(CreateResult("r-fresh-1", DateTime.UtcNow.AddDays(-1)));
        var service = CreateService(execDs, resultDs);

        var result = await service.CleanupAsync();

        Assert.Equal(0, result.DeletedExecutions);
        Assert.Equal(0, result.DeletedResults);
    }

    [Fact]
    public async Task CleanupAsync_BatchesUntilEmpty()
    {
        var (_, execDs, resultDs) = CreateHost();
        // CleanupBatchSize=2——5 条过期执行需分 3 轮清空（2+2+1），校验循环直至 < batchSize
        for (int i = 0; i < 5; i++)
            await execDs.EntityCreateAsync(CreateExecution($"batch-{i}", DateTime.UtcNow.AddDays(-200)));
        var service = CreateService(execDs, resultDs,
            new BackgroundJobsPersistenceOptions { CleanupBatchSize = 2 });

        var result = await service.CleanupAsync();

        Assert.Equal(5, result.DeletedExecutions);
        Assert.Empty(await GetAllExecutionIdsAsync(execDs));
    }

    [Fact]
    public async Task CleanupAsync_ExecutionCleanupFails_StillCleansResults()
    {
        // JobExecution 表未建（UseAutoSyncStructure(false) + 仅 Sync JobResult）→ 执行侧查询抛异常；
        // 异常静默对齐：LogWarning 记录，JobResult 仍清理
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(false)
            .Build();
        fsql.CodeFirst.SyncStructure<JobResultEntity>(); // 仅建 JobResult

        var brokenExecDs = new JobExecutionEntityDataService(new StubDomainUser(),
            new FreeSqlEntityDAC<JobExecutionEntity>(new UnitOfWorkManager(fsql)));
        var resultDs = new JobResultEntityDataService(new StubDomainUser(),
            new FreeSqlEntityDAC<JobResultEntity>(new UnitOfWorkManager(fsql)));
        await resultDs.EntityCreateAsync(CreateResult("r-ok-1", DateTime.UtcNow.AddDays(-200)));
        await resultDs.EntityCreateAsync(CreateResult("r-ok-2", DateTime.UtcNow.AddDays(-300)));

        var logger = new CaptureLogger<JobHistoryCleanupService>();
        var service = CreateService(brokenExecDs, resultDs, logger: logger);

        var result = await service.CleanupAsync();

        // 执行侧失败静默（0 删 + Warning），结果侧正常清理
        Assert.Equal(0, result.DeletedExecutions);
        Assert.Equal(2, result.DeletedResults);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("JobExecution"));
        Assert.Empty(await GetAllResultIdsAsync(resultDs));
    }

    [Fact]
    public void CleanupOptions_Defaults()
    {
        var options = new BackgroundJobsPersistenceOptions();
        Assert.Equal(180, options.RetentionDays);
        Assert.Equal(500, options.CleanupBatchSize);
    }
}

/// <summary>测试日志捕获器——记录所有日志条目（校验异常静默 Warning）。</summary>
internal sealed class CaptureLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}