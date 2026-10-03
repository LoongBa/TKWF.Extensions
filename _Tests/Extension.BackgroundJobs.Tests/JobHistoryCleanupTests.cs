using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
/// <para>V4.10.53（领域自治根治，正确路线）：</para>
/// <list type="bullet">
/// <item><strong>集成测试走生产路径</strong>——真实 DI（扩展 ConfigureServices + FreeSql 基础设施 +
///     真实 <see cref="DomainUser{TUserInfo}"/>）+ <c>User.Use&lt;IJobHistoryCleanupService&gt;()</c> 解析
///     （AOP 路径：设 CurrentAopUser → GetRequiredService → AddConstructibleService 构造工厂）；
///     种子经 <c>User.Use&lt;具体 DataService&gt;()</c> NoAop 直建（对齐 Settings 宿主）。</item>
/// <item><strong>单表失败容错保持分层单测</strong>——可配置 stub 直构清理服务（经基类 <c>User</c> 取上下文）
///     + CaptureLogger 精确捕获 Warning（§4.5 分层逻辑单测约定）。</item>
/// </list>
/// <para>断言容错——SQLite 读出 DateTime 可能 Unspecified/偏移，比较 Id 集合而非精确时间。</para>
/// </summary>
public class JobHistoryCleanupTests
{
    /// <summary>创建生产宿主（真实 DI + BindScope）——Options 可经后注册 Configure 覆盖（后注册后执行，属性覆盖生效）。</summary>
    private static (ServiceProvider Provider, DomainUser<TestUserInfo> User) CreateProductionHost(
        BackgroundJobsPersistenceOptions? options = null)
    {
        var services = new ServiceCollection();
        // 1. 扩展装配——生产形态（消费方白名单启用后三钩子执行 ConfigureServices）
        new BackgroundJobsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        // BindConfiguration("TKWF:BackgroundJobs") 需 IConfiguration（空配置——默认值兜底）
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // Options 覆盖（后注册——在 BindConfiguration 注册的配置动作之后执行，属性覆盖生效）
        if (options is not null)
            services.Configure<BackgroundJobsPersistenceOptions>(o =>
            {
                o.RetentionDays = options.RetentionDays;
                o.CleanupBatchSize = options.CleanupBatchSize;
            });

        // 2. FreeSql 基础设施（消费方 DomainHost 等价注册）
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<JobExecutionEntity>();
        fsql.CodeFirst.SyncStructure<JobResultEntity>();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<JobExecutionEntity>, FreeSqlEntityDAC<JobExecutionEntity>>();
        services.AddSingleton<IEntityDAC<JobResultEntity>, FreeSqlEntityDAC<JobResultEntity>>();

        // 3. 解析作用域绑定（生产经 DomainHost.NewDomainContext 绑定 AsyncLocal——测试等价 BindScope）
        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("cleanup-user", "清理用户") };
        return (provider, user);
    }

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

    /// <summary>生产路径种子：执行历史（NoAop 直建 DataService）。</summary>
    private static async Task<long> SeedExecutionAsync(DomainUser<TestUserInfo> user, string jobId, DateTime startedAtUtc)
        => (await user.Use<JobExecutionEntityDataService>().EntityCreateAsync(CreateExecution(jobId, startedAtUtc))).Id;

    /// <summary>生产路径种子：业务结果（NoAop 直建 DataService）。</summary>
    private static async Task<long> SeedResultAsync(DomainUser<TestUserInfo> user, string jobId, DateTime createTime)
        => (await user.Use<JobResultEntityDataService>().EntityCreateAsync(CreateResult(jobId, createTime))).Id;

    /// <summary>全部 JobExecution Id 集合（断言容错用）。</summary>
    private static async Task<List<long>> GetAllExecutionIdsAsync(DomainUser<TestUserInfo> user)
    {
        var rows = await user.Use<JobExecutionEntityDataService>().EntitySelectAsync(e => true, 0, 10_000);
        return rows.Select(e => e.Id).ToList();
    }

    /// <summary>全部 JobResult Id 集合（断言容错用）。</summary>
    private static async Task<List<long>> GetAllResultIdsAsync(DomainUser<TestUserInfo> user)
    {
        var rows = await user.Use<JobResultEntityDataService>().EntitySelectAsync(e => true, 0, 10_000);
        return rows.Select(e => e.Id).ToList();
    }

    /// <summary>分层单测直构清理服务（JobExecutionRecorder 无关的 stub 直构——§4.5；DataService 经 stub.With 注册懒加载源）。</summary>
    private static JobHistoryCleanupService CreateService(
        JobExecutionEntityDataService execDs, JobResultEntityDataService resultDs,
        BackgroundJobsPersistenceOptions? options = null, ILogger<JobHistoryCleanupService>? logger = null)
        => new(new StubDomainUser().With(execDs).With(resultDs),
            Options.Create(options ?? new BackgroundJobsPersistenceOptions()),
            logger ?? NullLogger<JobHistoryCleanupService>.Instance);

    // ═══════════════════════════════════════════════════════
    // V0.2.0: 历史清理（生产路径）
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task CleanupAsync_DeletesExpiredExecutions()
    {
        var (_, user) = CreateProductionHost();
        var expired = await SeedExecutionAsync(user, "expired-1", DateTime.UtcNow.AddDays(-200));
        await SeedExecutionAsync(user, "expired-2", DateTime.UtcNow.AddDays(-365));

        var result = await user.Use<IJobHistoryCleanupService>().CleanupAsync();

        Assert.Equal(2, result.DeletedExecutions);
        Assert.Equal(0, result.DeletedResults);
        Assert.DoesNotContain(expired, await GetAllExecutionIdsAsync(user));
    }

    [Fact]
    public async Task CleanupAsync_KeepsRecentExecutions()
    {
        var (_, user) = CreateProductionHost();
        var expired = await SeedExecutionAsync(user, "expired", DateTime.UtcNow.AddDays(-200));
        var recent = await SeedExecutionAsync(user, "recent", DateTime.UtcNow.AddDays(-1));

        var result = await user.Use<IJobHistoryCleanupService>().CleanupAsync();

        Assert.Equal(1, result.DeletedExecutions);
        var remaining = await GetAllExecutionIdsAsync(user);
        Assert.DoesNotContain(expired, remaining);
        Assert.Contains(recent, remaining);
    }

    [Fact]
    public async Task CleanupAsync_DeletesExpiredResults()
    {
        var (_, user) = CreateProductionHost();
        var expired = await SeedResultAsync(user, "r-expired-1", DateTime.UtcNow.AddDays(-200));
        await SeedResultAsync(user, "r-expired-2", DateTime.UtcNow.AddDays(-900));

        var result = await user.Use<IJobHistoryCleanupService>().CleanupAsync();

        Assert.Equal(0, result.DeletedExecutions);
        Assert.Equal(2, result.DeletedResults);
        Assert.DoesNotContain(expired, await GetAllResultIdsAsync(user));
    }

    [Fact]
    public async Task CleanupAsync_RetentionDaysFromOptions()
    {
        var (_, user) = CreateProductionHost(new BackgroundJobsPersistenceOptions { RetentionDays = 5 });
        // RetentionDays=5 → 仅清 5 天前的记录
        var old = await SeedExecutionAsync(user, "old-10d", DateTime.UtcNow.AddDays(-10));
        var recent = await SeedExecutionAsync(user, "recent-2d", DateTime.UtcNow.AddDays(-2));

        var result = await user.Use<IJobHistoryCleanupService>().CleanupAsync();

        Assert.Equal(1, result.DeletedExecutions);
        var remaining = await GetAllExecutionIdsAsync(user);
        Assert.DoesNotContain(old, remaining);
        Assert.Contains(recent, remaining);
    }

    [Fact]
    public async Task CleanupAsync_NoExpired_ReturnsZero()
    {
        var (_, user) = CreateProductionHost();
        await SeedExecutionAsync(user, "fresh-1", DateTime.UtcNow.AddDays(-1));
        await SeedResultAsync(user, "r-fresh-1", DateTime.UtcNow.AddDays(-1));

        var result = await user.Use<IJobHistoryCleanupService>().CleanupAsync();

        Assert.Equal(0, result.DeletedExecutions);
        Assert.Equal(0, result.DeletedResults);
    }

    [Fact]
    public async Task CleanupAsync_BatchesUntilEmpty()
    {
        var (_, user) = CreateProductionHost(new BackgroundJobsPersistenceOptions { CleanupBatchSize = 2 });
        // CleanupBatchSize=2——5 条过期执行需分 3 轮清空（2+2+1），校验循环直至 < batchSize
        for (int i = 0; i < 5; i++)
            await SeedExecutionAsync(user, $"batch-{i}", DateTime.UtcNow.AddDays(-200));

        var result = await user.Use<IJobHistoryCleanupService>().CleanupAsync();

        Assert.Equal(5, result.DeletedExecutions);
        Assert.Empty(await GetAllExecutionIdsAsync(user));
    }

    // ═══════════════════════════════════════════════════════
    // V0.2.0: 分层单测（精确失败控制 + Options 默认值）
    // ═══════════════════════════════════════════════════════

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
        var remaining = await resultDs.EntitySelectAsync(e => true, 0, 10_000);
        Assert.Empty(remaining);
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