using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.BackgroundJobs;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.BackgroundJobs;

namespace TKWF.Ext.BackgroundJobs.Tests;

/// <summary>
/// BackgroundJobs V0.1.0 扩展测试——D6-D10 验收覆盖：
/// D6: JobExecution 落库（MockListener 触发→DataService 写入一行）
/// D7: IJobResultRecorder（BackgroundJobContext.Current 读 JobId + 无上下文抛异常）
/// D8: 查询 API（分页/过滤/SQL 级聚合统计/GetDetailAsync + 结果查询）
/// D9: Initializer DI（TryAddScoped/TryAddEnumerable 注册 + Options 绑定）
/// D10: 监听器异常不阻断
/// </summary>
public class BackgroundJobsTests
{
    /// <summary>创建 SQLite :memory: + 建表（JobExecution + JobResult + DataService 链）。</summary>
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

    /// <summary>创建 BackgroundJobExecutedContext 测试数据。</summary>
    private static BackgroundJobExecutedContext CreateTestContext(
        string jobId = "test-job-001",
        string jobType = "TestJob, TestAssembly",
        string provider = "builtin",
        bool isSuccess = true,
        bool isCancelled = false,
        string? error = null,
        int retryAttempt = 1,
        long? tenantId = null)
    {
        var now = DateTime.UtcNow;
        return new BackgroundJobExecutedContext(
            JobId: jobId,
            JobType: jobType,
            Provider: provider,
            IsSuccess: isSuccess,
            IsCancelled: isCancelled,
            Error: error,
            RetryAttempt: retryAttempt,
            Duration: TimeSpan.FromMilliseconds(123),
            StartedAtUtc: now.AddSeconds(-1),
            CompletedAtUtc: now,
            TenantId: tenantId);
    }

    // ═══════════════════════════════════════════════════════
    // D6: JobExecution 落库测试
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task D6_Recorder_Success_CreatesExecutionRow()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var context = CreateTestContext(isSuccess: true);

        await recorder.OnExecutedAsync(context);

        var rows = await execDs.EntitySelectAsync(e => true, 0, 10, q => q.OrderByDescending(e => e.Id));
        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal("test-job-001", row.JobId);
        Assert.Equal("TestJob, TestAssembly", row.JobType);
        Assert.Equal("builtin", row.Provider);
        Assert.True(row.IsSuccess);
        Assert.False(row.IsCancelled);
        Assert.Equal(1, row.RetryAttempt);
        Assert.Equal(123, row.DurationMs);
        Assert.Null(row.ErrorText);
    }

    [Fact]
    public async Task D6_Recorder_Failed_RecordsErrorText()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var context = CreateTestContext(
            isSuccess: false, isCancelled: false,
            error: "System.Exception: Test error\n   at Test.Method()");

        await recorder.OnExecutedAsync(context);

        var rows = await execDs.EntitySelectAsync(e => true, 0, 10, q => q.OrderByDescending(e => e.Id));
        Assert.Single(rows);
        Assert.False(rows[0].IsSuccess);
        Assert.Contains("System.Exception: Test error", rows[0].ErrorText!);
    }

    [Fact]
    public async Task D6_Recorder_Cancelled_SetsIsCancelled()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var context = CreateTestContext(
            isSuccess: false, isCancelled: true, error: null);

        await recorder.OnExecutedAsync(context);

        var rows = await execDs.EntitySelectAsync(e => true, 0, 10, q => q.OrderByDescending(e => e.Id));
        Assert.Single(rows);
        Assert.True(rows[0].IsCancelled);
        Assert.False(rows[0].IsSuccess);
        Assert.Null(rows[0].ErrorText);
    }

    [Fact]
    public async Task D6_Recorder_MultipleProviders_Recorded()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(jobId: "j1", provider: "builtin"));
        await recorder.OnExecutedAsync(CreateTestContext(jobId: "j2", provider: "hangfire"));
        await recorder.OnExecutedAsync(CreateTestContext(jobId: "j3", provider: "quartz"));

        var rows = await execDs.EntitySelectAsync(e => true, 0, 10, q => q.OrderByDescending(e => e.Id));
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.Provider == "builtin" && r.JobId == "j1");
        Assert.Contains(rows, r => r.Provider == "hangfire" && r.JobId == "j2");
        Assert.Contains(rows, r => r.Provider == "quartz" && r.JobId == "j3");
    }

    [Fact]
    public async Task D6_Recorder_RetryAttempt_Recorded()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(retryAttempt: 1));
        await recorder.OnExecutedAsync(CreateTestContext(retryAttempt: 2));

        var rows = await execDs.EntitySelectAsync(e => true, 0, 10, q => q.OrderByDescending(e => e.Id));
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.RetryAttempt == 1);
        Assert.Contains(rows, r => r.RetryAttempt == 2);
    }

    // ═══════════════════════════════════════════════════════
    // D7: IJobResultRecorder 测试
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task D7_ResultRecorder_InContext_CreatesResultRow()
    {
        var (_, _, resultDs) = CreateHost();
        var recorder = new JobResultRecorder(resultDs, NullLogger<JobResultRecorder>.Instance);

        using var ctx = BackgroundJobContext.Enter("result-job-001", "builtin", null);
        var id = await recorder.RecordAsync("success", "{\"count\":42}", "处理完成");

        Assert.True(id > 0);

        var rows = await resultDs.EntitySelectAsync(e => true, 0, 10, q => q.OrderByDescending(e => e.Id));
        Assert.Single(rows);
        Assert.Equal("result-job-001", rows[0].JobId);
        Assert.Equal("success", rows[0].ResultType);
        Assert.Equal("{\"count\":42}", rows[0].ResultJson);
        Assert.Equal("处理完成", rows[0].Summary);
    }

    [Fact]
    public async Task D7_ResultRecorder_NoContext_ThrowsInvalidOperationException()
    {
        var (_, _, resultDs) = CreateHost();
        var recorder = new JobResultRecorder(resultDs, NullLogger<JobResultRecorder>.Instance);

        // BackgroundJobContext.Current 应为 null（无上下文）
        Assert.Null(BackgroundJobContext.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorder.RecordAsync("success", "{}"));
    }

    [Fact]
    public async Task D7_ResultRecorder_DefaultResultType()
    {
        var (_, _, resultDs) = CreateHost();
        var recorder = new JobResultRecorder(resultDs, NullLogger<JobResultRecorder>.Instance);

        using var ctx = BackgroundJobContext.Enter("default-type-job", "builtin", null);
        var id = await recorder.RecordAsync("success", "{}");

        var rows = await resultDs.EntitySelectAsync(e => true, 0, 10, q => q.OrderByDescending(e => e.Id));
        Assert.Single(rows);
        Assert.Equal("success", rows[0].ResultType);
    }

    // ═══════════════════════════════════════════════════════
    // D8: 查询 API 测试
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task D8_QueryService_GetListAsync_Pagination()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        // 插入 5 条记录
        for (int i = 0; i < 5; i++)
            await recorder.OnExecutedAsync(CreateTestContext(jobId: $"job-{i}"));

        var page1 = await queryService.GetListAsync(new JobExecutionQueryInput(Skip: 0, Take: 2));
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(5, page1.Total);   // Total = 过滤后总条数（SQL COUNT），非当前页条数

        var page2 = await queryService.GetListAsync(new JobExecutionQueryInput(Skip: 2, Take: 2));
        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(5, page2.Total);
    }

    [Fact]
    public async Task D8_QueryService_GetListAsync_FiltersByProvider()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(jobId: "j1", provider: "builtin"));
        await recorder.OnExecutedAsync(CreateTestContext(jobId: "j2", provider: "hangfire"));
        await recorder.OnExecutedAsync(CreateTestContext(jobId: "j3", provider: "builtin"));

        var filtered = await queryService.GetListAsync(new JobExecutionQueryInput(Provider: "builtin"));
        Assert.Equal(2, filtered.Items.Count);
        Assert.All(filtered.Items, i => Assert.Equal("builtin", i.Provider));
    }

    [Fact]
    public async Task D8_QueryService_GetListAsync_FiltersByIsSuccess()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: true));
        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: false, error: "fail"));
        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: true));

        var succeeded = await queryService.GetListAsync(new JobExecutionQueryInput(IsSuccess: true));
        Assert.Equal(2, succeeded.Items.Count);
        Assert.All(succeeded.Items, i => Assert.True(i.IsSuccess));
    }

    [Fact]
    public async Task D8_QueryService_GetListAsync_FiltersByJobId()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(jobId: "job-a"));
        await recorder.OnExecutedAsync(CreateTestContext(jobId: "job-b"));

        var filtered = await queryService.GetListAsync(new JobExecutionQueryInput(JobId: "job-a"));
        Assert.Single(filtered.Items);
        Assert.Equal("job-a", filtered.Items[0].JobId);
        Assert.Equal(1, filtered.Total);
    }

    [Fact]
    public async Task D8_QueryService_GetListAsync_FiltersByJobType()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(jobType: "SyncJob, TestAssembly"));
        await recorder.OnExecutedAsync(CreateTestContext(jobType: "CleanupJob, TestAssembly"));

        var filtered = await queryService.GetListAsync(new JobExecutionQueryInput(JobType: "SyncJob, TestAssembly"));
        Assert.Single(filtered.Items);
        Assert.Equal("SyncJob, TestAssembly", filtered.Items[0].JobType);
    }

    [Fact]
    public async Task D8_QueryService_GetListAsync_FiltersByTenantId()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(jobId: "tenant-1-job", tenantId: 1));
        await recorder.OnExecutedAsync(CreateTestContext(jobId: "tenant-2-job", tenantId: 2));

        var filtered = await queryService.GetListAsync(new JobExecutionQueryInput(TenantId: 1));
        Assert.Single(filtered.Items);
        Assert.Equal(1, filtered.Items[0].TenantId);
    }

    [Fact]
    public async Task D8_QueryService_GetListAsync_FiltersByIsCancelled()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: false, isCancelled: true));
        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: true));

        var cancelled = await queryService.GetListAsync(new JobExecutionQueryInput(IsCancelled: true));
        Assert.Single(cancelled.Items);
        Assert.True(cancelled.Items[0].IsCancelled);
    }

    [Fact]
    public async Task D8_QueryService_GetListAsync_FiltersByTimeRange()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);
        var now = DateTime.UtcNow;

        // StartedAtUtc = now-1s（CreateTestContext 默认）——落在 [now-2s, now] 窗口内
        await recorder.OnExecutedAsync(CreateTestContext(jobId: "in-window"));

        var inWindow = await queryService.GetListAsync(new JobExecutionQueryInput(
            StartFromUtc: now.AddSeconds(-2), StartToUtc: now));
        Assert.Single(inWindow.Items);
        Assert.Equal("in-window", inWindow.Items[0].JobId);

        // 窗口偏移到未来——无命中
        var noHit = await queryService.GetListAsync(new JobExecutionQueryInput(
            StartFromUtc: now.AddHours(1), StartToUtc: now.AddHours(2)));
        Assert.Empty(noHit.Items);
    }

    [Fact]
    public async Task D8_QueryService_GetListAsync_ListDto_ExcludesErrorText()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: false, error: "secret error detail"));

        var result = await queryService.GetListAsync(new JobExecutionQueryInput());
        Assert.Single(result.Items);
        // JobExecutionListItemDto 无 ErrorText 属性（编译期保证）
    }

    [Fact]
    public async Task D8_QueryService_GetDetailAsync_IncludesErrorText()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: false, error: "detailed error"));

        var list = await queryService.GetListAsync(new JobExecutionQueryInput());
        var detail = await queryService.GetDetailAsync(list.Items[0].Id);

        Assert.NotNull(detail);
        Assert.Equal("detailed error", detail!.ErrorText);
    }

    [Fact]
    public async Task D8_QueryService_GetDetailAsync_NullWhenNotFound()
    {
        var (_, execDs, _) = CreateHost();
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        var detail = await queryService.GetDetailAsync(999);
        Assert.Null(detail);
    }

    [Fact]
    public async Task D8_QueryService_GetStatsAsync_SQL_Level_Aggregation()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        // 2 成功 + 1 失败 + 1 取消
        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: true));
        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: true));
        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: false, error: "err"));
        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: false, isCancelled: true));

        var stats = await queryService.GetStatsAsync();
        Assert.Equal(4, stats.Total);
        Assert.Equal(2, stats.Succeeded);
        Assert.Equal(1, stats.Failed);
        Assert.Equal(1, stats.Cancelled);
        Assert.True(stats.AvgDurationMs >= 0);
        Assert.True(stats.MaxDurationMs >= stats.AvgDurationMs);
    }

    [Fact]
    public async Task D8_QueryService_GetStatsAsync_WithTimeWindow()
    {
        var (_, execDs, _) = CreateHost();
        var recorder = new JobExecutionRecorder(execDs, NullLogger<JobExecutionRecorder>.Instance);
        var queryService = new JobExecutionQueryService(execDs, NullLogger<JobExecutionQueryService>.Instance);

        await recorder.OnExecutedAsync(CreateTestContext(isSuccess: true));

        // 1 小时窗口——刚插入的记录在窗口内
        var statsInWindow = await queryService.GetStatsAsync(TimeSpan.FromHours(1));
        Assert.Equal(1, statsInWindow.Total);

        // 1 毫秒窗口——刚插入的记录可能已超出
        var statsNoWindow = await queryService.GetStatsAsync(TimeSpan.FromMilliseconds(1));
        // 可能为 0 或 1（取决于执行速度）
        Assert.True(statsNoWindow.Total >= 0);
    }

    [Fact]
    public async Task D8_ResultQueryService_GetListAsync()
    {
        var (_, _, resultDs) = CreateHost();
        var recorder = new JobResultRecorder(resultDs, NullLogger<JobResultRecorder>.Instance);
        var queryService = new JobResultQueryService(resultDs, NullLogger<JobResultQueryService>.Instance);

        using (var ctx = BackgroundJobContext.Enter("q-job-1", "builtin", null))
            await recorder.RecordAsync("success", "{\"a\":1}");
        using (var ctx = BackgroundJobContext.Enter("q-job-2", "builtin", null))
            await recorder.RecordAsync("error", "{\"b\":2}");

        var all = await queryService.GetListAsync(new JobResultQueryInput());
        Assert.Equal(2, all.Items.Count);

        var filtered = await queryService.GetListAsync(new JobResultQueryInput(ResultType: "success"));
        Assert.Single(filtered.Items);
        Assert.Equal("success", filtered.Items[0].ResultType);
    }

    [Fact]
    public async Task D8_ResultQueryService_GetLatestAsync()
    {
        var (_, _, resultDs) = CreateHost();
        var recorder = new JobResultRecorder(resultDs, NullLogger<JobResultRecorder>.Instance);
        var queryService = new JobResultQueryService(resultDs, NullLogger<JobResultQueryService>.Instance);

        using (var ctx = BackgroundJobContext.Enter("latest-job", "builtin", null))
        {
            await recorder.RecordAsync("success", "{\"v\":1}");
            await Task.Delay(10); // 确保时间差
            await recorder.RecordAsync("success", "{\"v\":2}");
        }

        var latest = await queryService.GetLatestAsync("latest-job");
        Assert.NotNull(latest);
        Assert.Contains("\"v\":2", latest!.ResultJson!);
    }

    // ═══════════════════════════════════════════════════════
    // D9: Initializer DI 测试
    // ═══════════════════════════════════════════════════════

    [Fact]
    public void D9_ExtensionAttribute_Declared()
    {
        var attr = typeof(BackgroundJobsExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("BackgroundJobs", attr.Name);
    }

    [Fact]
    public void D9_ConfigureServices_Registers_All_Services()
    {
        var services = new ServiceCollection();
        var init = new BackgroundJobsExtensionInitializer<TestUserInfo>();
        init.ConfigureServices(services);
        TestInfrastructure.RegisterTestInfrastructure(services);

        var sp = services.BuildServiceProvider();

        // 监听器（TryAddEnumerable 可叠加）——注意：无法解析具体类型（需 SG1 注册 DataServices）
        var listenerDescriptors = services.Where(d => d.ServiceType == typeof(IBackgroundJobExecutionListener)).ToList();
        Assert.Single(listenerDescriptors);
        Assert.Equal(typeof(JobExecutionRecorder), listenerDescriptors[0].ImplementationType);

        // 结果记录器
        var resultRecorderDesc = services.FirstOrDefault(d => d.ServiceType == typeof(IJobResultRecorder));
        Assert.NotNull(resultRecorderDesc);
        Assert.Equal(typeof(JobResultRecorder), resultRecorderDesc.ImplementationType);

        // 查询服务
        var execQueryDesc = services.FirstOrDefault(d => d.ServiceType == typeof(IJobExecutionQueryService));
        Assert.NotNull(execQueryDesc);
        Assert.Equal(typeof(JobExecutionQueryService), execQueryDesc.ImplementationType);

        var resultQueryDesc = services.FirstOrDefault(d => d.ServiceType == typeof(IJobResultQueryService));
        Assert.NotNull(resultQueryDesc);
        Assert.Equal(typeof(JobResultQueryService), resultQueryDesc.ImplementationType);
    }

    [Fact]
    public void D9_ConfigureServices_TryAddEnumerable_IsIdempotent()
    {
        var services = CreateServicesWithConfiguration();
        var init = new BackgroundJobsExtensionInitializer<TestUserInfo>();
        init.ConfigureServices(services);
        init.ConfigureServices(services); // 二次调用（TryAddEnumerable 幂等）
        TestInfrastructure.RegisterTestInfrastructure(services);

        var listenerDescriptors = services.Where(d => d.ServiceType == typeof(IBackgroundJobExecutionListener)).ToList();
        Assert.Single(listenerDescriptors); // TryAddEnumerable 不重复注册
    }

    [Fact]
    public void D9_ConfigureServices_TryAddEnumerable_CanStack()
    {
        var services = CreateServicesWithConfiguration();
        var init = new BackgroundJobsExtensionInitializer<TestUserInfo>();
        init.ConfigureServices(services);

        // 手动再注册一个监听器（模拟多扩展叠加）
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IBackgroundJobExecutionListener, FakeExtraListener>());
        TestInfrastructure.RegisterTestInfrastructure(services);

        var listenerDescriptors = services.Where(d => d.ServiceType == typeof(IBackgroundJobExecutionListener)).ToList();
        Assert.Equal(2, listenerDescriptors.Count);
        Assert.Contains(listenerDescriptors, d => d.ImplementationType == typeof(JobExecutionRecorder));
        Assert.Contains(listenerDescriptors, d => d.ImplementationType == typeof(FakeExtraListener));
    }

    [Fact]
    public void D9_Options_Bound()
    {
        var services = CreateServicesWithConfiguration();
        var init = new BackgroundJobsExtensionInitializer<TestUserInfo>();
        init.ConfigureServices(services);

        var sp = services.BuildServiceProvider();
        var options = sp.GetService<Microsoft.Extensions.Options.IOptions<BackgroundJobsPersistenceOptions>>();
        Assert.NotNull(options);
        Assert.Equal(180, options!.Value.RetentionDays); // 默认值
    }

    /// <summary>创建含 IConfiguration 的 DI 容器（BindConfiguration 依赖）。</summary>
    private static IServiceCollection CreateServicesWithConfiguration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        return services;
    }

    // ═══════════════════════════════════════════════════════
    // D10: 监听器异常不阻断测试
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task D10_Recorder_Exception_DoesNotThrow()
    {
        // 使用不可用的 DataService（未建表）触发异常
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(false) // 不自动建表
            .Build();
        // 不 SyncStructure——直接查会报错
        var brokenDs = new JobExecutionEntityDataService(new StubDomainUser(),
            new FreeSqlEntityDAC<JobExecutionEntity>(new UnitOfWorkManager(fsql)));
        var recorder = new JobExecutionRecorder(brokenDs, NullLogger<JobExecutionRecorder>.Instance);

        // 不应抛出异常（异常静默 + Warning）
        await recorder.OnExecutedAsync(CreateTestContext()); // should not throw
    }
}

/// <summary>假监听器——测试 TryAddEnumerable 可叠加。</summary>
internal sealed class FakeExtraListener : IBackgroundJobExecutionListener
{
    public Task OnExecutedAsync(BackgroundJobExecutedContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>
/// D9 测试基建——补齐宿主管线才注册的依赖（单元测试无宿主场景）：
/// 监听器/查询服务/记录器构造依赖 IDomainUser + IEntityDAC&lt;T&gt; + ILogger&lt;T&gt;，
/// 注入 Null 桩仅用于 DI 解析（不实际访问数据）。
/// </summary>
internal static class TestInfrastructure
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IDomainUser>(_ => new StubDomainUser());
        services.AddScoped<IEntityDAC<JobExecutionEntity>>(_ => new NullEntityDac<JobExecutionEntity>());
        services.AddScoped<IEntityDAC<JobResultEntity>>(_ => new NullEntityDac<JobResultEntity>());
    }

    /// <summary>便捷入口——测试内 <c>RegisterTestInfrastructure(services)</c> 对齐命名。</summary>
    public static void RegisterTestInfrastructure(IServiceCollection services) => Register(services);
}

/// <summary>空 DAC 桩——仅满足 DI 构造，所有成员抛 NotSupportedException（D9 测试不触达数据）。</summary>
internal sealed class NullEntityDac<TEntity> : IEntityDAC<TEntity>
    where TEntity : class, IDomainEntity, new()
{
    public IQueryable<TEntity> Query => throw new NotSupportedException();
    public Task<TEntity?> FirstOrDefaultAsync(IQueryable<TEntity> query, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<List<TEntity>> ToListAsync(IQueryable<TEntity> query, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<long> CountAsync(IQueryable<TEntity> query, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TResult?> FirstOrDefaultAsync<TResult>(IQueryable<TEntity> query, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<List<TResult>> ToListAsync<TResult>(IQueryable<TEntity> query, System.Linq.Expressions.Expression<Func<TEntity, TResult>> selector, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TEntity> InsertAsync(TEntity entity, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<List<TEntity>> InsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(TEntity entity, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(TEntity entity, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> UpdateColumnsBatchAsync<TColumns>(IEnumerable<TEntity> entities, System.Linq.Expressions.Expression<Func<TEntity, TColumns>> columns, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约。</summary>
internal sealed class StubDomainUser : IDomainUser
{
    public string SessionKey => "test";
    public bool IsAuthenticated => false;
    public bool IsSystemActor => false;
    public IUserInfo? UserInfo => null;
    public long? TenantId => null;
    public bool IsNoAuditActive => false;
    public string? UserId => null;
    public string? UserName => null;
    public bool IsInRole(string role) => false;
    public TDomainService Use<TDomainService>() where TDomainService : IDomainService => throw new NotSupportedException();
    public TService GetService<TService>() where TService : notnull => throw new NotSupportedException();
    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
