using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using miniExcel = MiniExcelLibs;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.DataPort;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.DataPort.Tests;

/// <summary>
/// V0.1.5（DP-05）Processing 超时治理测试——T5-T8（方案 §4.4）：
/// <list type="bullet">
/// <item>T5 超时记录被恢复（CAS：UpdateTime &lt; 阈值 → Failed + "stale recovery" 标记 + EndTime）；</item>
/// <item>T6 未超时记录不受影响（活跃 Processing / 心跳 touch 后——并发单飞保护 + 心跳路径）；</item>
/// <item>T7 启动扫描接线（Initializer.InitializeAsync System 作用域）——恢复 + 异常静默（数据服务缺省不阻断）；</item>
/// <item>T8 恢复后重导闭环 + Options 覆盖 + CAS 并发 + 心跳 CAS（DP-04×DP-05 联动）。</item>
/// </list>
/// 宿主沿用 <see cref="DataPortTestHost.Create"/>（生产路径 AOP + 真实 DI）；恢复门面经
/// <c>host.RecoveryService</c>（User.Use&lt;IStaleImportRecoveryService&gt;()）解析；CAS 经 DataService 直验。
/// </summary>
public class StaleImportRecoveryServiceTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(60);

    /// <summary>T5：超时记录被恢复——UpdateTime 拨到阈值前（60 分钟 + 1 秒）→ RecoverStaleImportsAsync → Failed + "stale recovery" + EndTime 非空；返回 1。</summary>
    [Fact]
    public async Task T5_StaleProcessing_RecoveredAsFailed()
    {
        using var host = DataPortTestHost.Create();
        var staleBefore = DateTime.UtcNow.AddMinutes(-DefaultTimeout.TotalMinutes - 1);

        var record = await SeedProcessingRecordAsync(host, staleBefore, TestContext.Current.CancellationToken);

        var recovered = await host.RecoveryService.RecoverStaleImportsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, recovered);
        var updated = await host.DataService.EntityGetAsync(r => r.Id == record.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("Failed", updated!.Status);
        Assert.Contains("stale recovery", updated.ErrorSummary ?? "");
        Assert.NotNull(updated.EndTime);
    }

    /// <summary>T6：未超时记录不受影响——UpdateTime 新于阈值（活跃 Processing）；心跳 touch 后同样不被恢复。恢复返 0，状态仍 Processing。</summary>
    [Fact]
    public async Task T6_FreshProcessing_NotRecovered()
    {
        using var host = DataPortTestHost.Create();
        var fresh = await SeedProcessingRecordAsync(host, DateTime.UtcNow.AddMinutes(-5), TestContext.Current.CancellationToken);
        var heartbeatTouched = await SeedProcessingRecordAsync(host, DateTime.UtcNow.AddMinutes(-DefaultTimeout.TotalMinutes - 1), TestContext.Current.CancellationToken);
        await host.DataService.TouchProcessingAsync(heartbeatTouched.Id, DateTime.UtcNow, TestContext.Current.CancellationToken);   // 心跳 touch 刷新 UpdateTime

        var recovered = await host.RecoveryService.RecoverStaleImportsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, recovered);   // 活跃 Processing + 心跳 touch 后均不误清
        var freshAfter = await host.DataService.EntityGetAsync(r => r.Id == fresh.Id, TestContext.Current.CancellationToken);
        var touchedAfter = await host.DataService.EntityGetAsync(r => r.Id == heartbeatTouched.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Processing", freshAfter!.Status);
        Assert.Equal("Processing", touchedAfter!.Status);
    }

    /// <summary>T7：启动扫描接线——宿主内直接调 Initializer.InitializeAsync（System 作用域）→ 超时记录被恢复；异常静默。</summary>
    [Fact]
    public async Task T7_StartupScan_RecoversStaleRecords()
    {
        using var host = DataPortTestHost.Create();
        var staleBefore = DateTime.UtcNow.AddMinutes(-DefaultTimeout.TotalMinutes - 1);
        var record = await SeedProcessingRecordAsync(host, staleBefore, TestContext.Current.CancellationToken);

        // 模拟消费方启动链（StandaloneDomainUserAccessor 对齐 BackgroundJobs V0.4.1 先例）→ InitializeAsync 执行启动扫描
        StandaloneDomainUserAccessor<TestUserInfo>.SetCurrentUser(new DomainUser<TestUserInfo>());
        try
        {
            var initializer = new DataPortExtensionInitializer<TestUserInfo>();
            await initializer.InitializeAsync(host.ServiceProvider);

            var updated = await host.DataService.EntityGetAsync(r => r.Id == record.Id, TestContext.Current.CancellationToken);
            Assert.Equal("Failed", updated!.Status);
            Assert.Contains("stale recovery", updated.ErrorSummary ?? "");
        }
        finally
        {
            StandaloneDomainUserAccessor<TestUserInfo>.Clear();
        }
    }

    /// <summary>T7-静默：异常静默——无 StandaloneDomainUserAccessor 绑定 / 数据服务不可用时 InitializeAsync 不抛（LogWarning）。</summary>
    [Fact]
    public async Task T7_StartupScan_ExceptionSilent()
    {
        using var host = DataPortTestHost.Create();
        // 未 SetCurrentUser → StandaloneDomainUserAccessor.DomainUser 抛 InvalidOperationException（decompile 实证）→
        // 被 InitializeAsync try/catch 捕获 → LogWarning 不抛（"绝不阻断宿主启动"铁律）
        var initializer = new DataPortExtensionInitializer<TestUserInfo>();
        await initializer.InitializeAsync(host.ServiceProvider);   // 不应抛
    }

    /// <summary>T8：恢复后重导闭环 + Options 覆盖 + CAS 并发 + 心跳 CAS（DP-04×DP-05 联动）。</summary>
    [Fact]
    public async Task T8_RecoveryReimportLoop_OptionsOverride_Cas_Heartbeat()
    {
        // ① 恢复后重导闭环：首导 PartiallySucceeded → 拨旧 UpdateTime → 恢复为 Failed → 同文件 ImportAsync →
        //    全量重导（Failed 语义）→ 终态 Succeeded
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(3);
        try
        {
            var firstAdapter = new DataImportTaskServiceTests.CountingBatchAdapter(0);
            var first = await host.TaskService.ImportAsync(filePath, firstAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), TestContext.Current.CancellationToken);
            Assert.Equal("PartiallySucceeded", (await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, TestContext.Current.CancellationToken))!.Status);

            // 模拟进程崩溃冻结：记录停留 Processing（清 EndTime）+ UpdateTime 拨旧（超过超时阈值）
            var staleRec = await host.DataService.EntityGetAsync(r => r.Id == first.RecordId, TestContext.Current.CancellationToken);
            staleRec!.Status = "Processing";
            staleRec.EndTime = null;
            staleRec.UpdateTime = DateTime.UtcNow.AddMinutes(-DefaultTimeout.TotalMinutes - 1);
            await host.DataService.EntityUpdateAsync(staleRec, TestContext.Current.CancellationToken);

            var recovered = await host.RecoveryService.RecoverStaleImportsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, recovered);
            Assert.Equal("Failed", (await host.DataService.EntityGetAsync(r => r.Id == first.RecordId, TestContext.Current.CancellationToken))!.Status);

            // 恢复出的 Failed 记录 → 同文件重导 → 全量重导成功
            var reimportAdapter = new DataImportTaskServiceTests.CountingBatchAdapter();
            var reimport = await host.TaskService.ImportAsync(filePath, reimportAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), TestContext.Current.CancellationToken);
            Assert.Equal(first.BatchNo, reimport.BatchNo);
            Assert.Equal(new[] { 0, 1 }, reimportAdapter.CalledBatchIndexes(2));   // Failed 全量重导（非重放过滤）
            Assert.Equal("Succeeded", (await host.DataService.EntityGetAsync(r => r.Id == first.RecordId, TestContext.Current.CancellationToken))!.Status);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }

        // ② Options 覆盖：ProcessingTimeoutMinutes=1 → 1 分钟前即超时
        using var shortHost = DataPortTestHost.Create(new DataPortOptions { ProcessingTimeoutMinutes = 1 });
        var shortRec = await SeedProcessingRecordAsync(shortHost, DateTime.UtcNow.AddMinutes(-2), TestContext.Current.CancellationToken);
        var shortRecovered = await shortHost.RecoveryService.RecoverStaleImportsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, shortRecovered);
        Assert.Equal("Failed", (await shortHost.DataService.EntityGetAsync(r => r.Id == shortRec.Id, TestContext.Current.CancellationToken))!.Status);

        // ③ CAS 并发断言：scan 后手改记录为 Succeeded 再调恢复 → 恢复不动该记录（MarkStaleFailedAsync 条件更新影响 0 行）
        var casRec = await SeedProcessingRecordAsync(shortHost, DateTime.UtcNow.AddMinutes(-5), TestContext.Current.CancellationToken);
        var succeeded = await shortHost.DataService.EntityGetAsync(r => r.Id == casRec.Id, TestContext.Current.CancellationToken);
        succeeded!.Status = "Succeeded";
        await shortHost.DataService.EntityUpdateAsync(succeeded, TestContext.Current.CancellationToken);
        var affected = await shortHost.DataService.MarkStaleFailedAsync(
            casRec.Id, DateTime.UtcNow.AddMinutes(-1), "cas-override", DateTime.UtcNow, TestContext.Current.CancellationToken);
        Assert.Equal(0, affected);   // Status != Processing → 条件更新不匹配 → 不覆写终态
        Assert.Equal("Succeeded", (await shortHost.DataService.EntityGetAsync(r => r.Id == casRec.Id, TestContext.Current.CancellationToken))!.Status);

        // ④ 心跳 CAS：仅 Processing 态刷新 UpdateTime（返回影响行 1）；终态 touch 返回 0（不覆写）
        using var heartbeatHost = DataPortTestHost.Create(new DataPortOptions { HeartbeatIntervalMinutes = 1 });
        var beatRec = await SeedProcessingRecordAsync(heartbeatHost, DateTime.UtcNow.AddMinutes(-DefaultTimeout.TotalMinutes - 1), TestContext.Current.CancellationToken);
        var beatTouched = await heartbeatHost.DataService.TouchProcessingAsync(beatRec.Id, DateTime.UtcNow, TestContext.Current.CancellationToken);
        Assert.Equal(1, beatTouched);
        var beatAfter = await heartbeatHost.DataService.EntityGetAsync(r => r.Id == beatRec.Id, TestContext.Current.CancellationToken);
        Assert.True(DateTime.UtcNow - beatAfter!.UpdateTime < TimeSpan.FromMinutes(1));   // UpdateTime 已刷新（距 now < 1 分钟）
        var recoveredAfterHeartbeat = await heartbeatHost.RecoveryService.RecoverStaleImportsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, recoveredAfterHeartbeat);   // 心跳已刷新 → 未超时 → 不清
        // 终态 touch：Succeeded 后 touch 返回 0（CAS 不覆写）
        beatAfter.Status = "Succeeded";
        await heartbeatHost.DataService.EntityUpdateAsync(beatAfter, TestContext.Current.CancellationToken);
        var terminalTouch = await heartbeatHost.DataService.TouchProcessingAsync(beatRec.Id, DateTime.UtcNow, TestContext.Current.CancellationToken);
        Assert.Equal(0, terminalTouch);
    }

    /// <summary>种子：直接以指定 UpdateTime 创建 Processing 记录（经生产 DataService）。</summary>
    private static async Task<DataImportRecordEntity> SeedProcessingRecordAsync(
        DataPortTestHost host, DateTime updateTime, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entity = new DataImportRecordEntity
        {
            BatchNo = Guid.NewGuid().ToString("N"),
            FileHash = Guid.NewGuid().ToString("N"),
            FileName = "stale-seed.xlsx",
            ProviderName = "Seed",
            Status = "Processing",
            StartTime = now,
            CreateTime = now,
            UpdateTime = updateTime
        };
        return await host.DataService.EntityCreateAsync(entity, ct);
    }
}

/// <summary>跨测试类共享测试数据（T1-T11 / T5-T8 计数适配器语义一致——可观测写调用批次集合）。</summary>
internal static class ImportServiceTestData
{
    /// <summary>创建含 RowNo 列的临时 xlsx（行号 0..N-1，Amount 递增，均 &gt; 0）。</summary>
    public static async Task<string> CreateTempDataFileAsync(int rowCount = 3)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"dataport_test_{System.Guid.NewGuid():N}.xlsx");
        var rows = new List<System.Collections.Generic.Dictionary<string, object>>();
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(new System.Collections.Generic.Dictionary<string, object>
            {
                ["RowNo"] = i,
                ["Name"] = ((char)('A' + i)).ToString(),
                ["Amount"] = 100m + i
            });
        }
        await miniExcel.MiniExcel.SaveAsAsync(path, rows, printHeader: true);
        return path;
    }
}