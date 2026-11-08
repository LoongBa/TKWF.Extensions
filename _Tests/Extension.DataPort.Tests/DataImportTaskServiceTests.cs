using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using miniExcel = MiniExcelLibs;
using TKW.Framework.Utility.DataPort;
using TKW.Framework.Utility.DataPort.Providers.MiniExcel;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.DataPort.Tests;

/// <summary>
/// DataImportTaskService 测试——使用 SQLite 内存库 + MiniExcel 真实读写引擎验证
/// 批次记录落库（Processing→Succeeded/Failed/PartiallySucceeded）+ FileHash 幂等 + 按 BatchNo 查询。
/// <para>V4.10.53（领域自治根治，ADR90，正确路线）宿主改走<b>生产路径</b>（对齐 Settings/FileManagement）：
/// 真实 DI（Initializer ConfigureServices + FreeSql 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope</c>
/// → 门面经 <c>User.Use&lt;IDataImportTaskService&gt;()</c> AOP 路径解析（AddConstructibleService 守卫工厂）。
/// 原 StubDomainUser + 手写 DataService 直构已删除——IDomainUser 永不注册 DI 语义在生产路径下自然保持。</para>
/// <para>V0.1.5（DP-04/DP-05）新增 T1-T4/T9-T11：PartiallySucceeded 失败批次重放（definition 模式 onBatch 过滤——
/// 断言改用 CountingBatchAdapter 的 CalledRowGroups/业务表行数，无包装器内部计数器）+ BatchSize 一致性拒绝 +
/// 解析失败降级全量；DP-05 T5-T8 见 <see cref="StaleImportRecoveryServiceTests"/>。T1-T4 可观测断言
/// （方案 §3.4）：CalledBatchIndexes 由行号组推导（min RowNo / BatchSize），零包装器计数耦合。</para>
/// </summary>
public class DataImportTaskServiceTests
{
    /// <summary>创建含 3 行数据（A/B/C，金额均 &gt; 0）的临时 xlsx 文件（兼容既有 3 行用例）。</summary>
    private static async Task<string> CreateTempDataFileAsync()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"dataport_test_{System.Guid.NewGuid():N}.xlsx");
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["Name"] = "A", ["Amount"] = 100m },
            new() { ["Name"] = "B", ["Amount"] = 200m },
            new() { ["Name"] = "C", ["Amount"] = 300m }
        };
        await miniExcel.MiniExcel.SaveAsAsync(path, rows, printHeader: true);
        return path;
    }

    [Fact]
    public async Task ImportAsync_NewFile_CreatesRecordWithSucceededStatus()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await CreateTempDataFileAsync();

        try
        {
            var result = await host.TaskService.ImportAsync(filePath, new TestImportAdapter(), ct: TestContext.Current.CancellationToken);

            Assert.True(result.RecordId > 0);
            Assert.False(string.IsNullOrWhiteSpace(result.BatchNo));
            Assert.Equal(3, result.ImportResult.SuccessCount);
            Assert.Empty(result.ImportResult.Failures);
            Assert.Empty(result.ImportResult.BatchFailures);

            var record = await host.TaskService.GetRecordByBatchNoAsync(result.BatchNo, CancellationToken.None);

            Assert.NotNull(record);
            Assert.Equal(result.BatchNo, record!.BatchNo);
            Assert.Equal("Succeeded", record.Status);
            Assert.Equal(3, record.SuccessCount);
            Assert.Equal(0, record.FailedCount);
            Assert.Equal(0, record.BatchFailureCount);
            Assert.Equal("TestImportAdapter", record.ProviderName);
            Assert.NotNull(record.EndTime);
            // V0.1.5（DP-04）：Succeeded 终态——三新列语义（FailedBatchIndexes=null、BatchSize 首跑落库、MaxAttemptedBatchIndex=本次最大批次）
            Assert.Null(record.FailedBatchIndexes);
            Assert.Equal(500, record.BatchSize);   // 默认选项 DefaultBatchSize=500 → 3 行仅 1 批
            Assert.Equal(0, record.MaxAttemptedBatchIndex);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_SameFileSecondTime_ReturnsExistingBatch()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await CreateTempDataFileAsync();

        try
        {
            var first = await host.TaskService.ImportAsync(filePath, new TestImportAdapter(), ct: TestContext.Current.CancellationToken);
            var second = await host.TaskService.ImportAsync(filePath, new TestImportAdapter(), ct: TestContext.Current.CancellationToken);

            // 幂等：同一文件（FileHash 相同）二次导入 → 拒绝/返回已有批次
            Assert.Equal(first.BatchNo, second.BatchNo);
            Assert.Equal(first.RecordId, second.RecordId);
            Assert.Same(ImportResult.Empty, second.ImportResult);

            // 数据库仅一条批次记录（幂等：二次导入命中同一批次，无新增行）
            var record = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.NotNull(record);
            Assert.Equal(first.RecordId, record!.Id);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_FailedBatch_MarksPartiallySucceeded()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await CreateTempDataFileAsync();

        try
        {
            // BatchSize=2：首批（行 1-2）持久化抛错 → BatchFailure；末批（行 3）成功
            var adapter = new ThrowingBatchAdapter();
            var batchOptions = new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false);

            var result = await host.TaskService.ImportAsync(filePath, adapter, batchOptions, ct: TestContext.Current.CancellationToken);

            Assert.NotEmpty(result.ImportResult.BatchFailures);
            Assert.Equal(2, adapter.BatchCount);

            var record = await host.TaskService.GetRecordByBatchNoAsync(result.BatchNo, CancellationToken.None);

            Assert.NotNull(record);
            Assert.Equal("PartiallySucceeded", record!.Status);
            Assert.Equal(3, record.SuccessCount);
            Assert.Equal(1, record.BatchFailureCount);
            Assert.False(string.IsNullOrWhiteSpace(record.ErrorSummary));
            Assert.NotNull(record.EndTime);
            // V0.1.5（DP-04）：PartiallySucceeded 终态——失败批次索引结构化落库（重放判定依据）
            Assert.Equal("[0]", record.FailedBatchIndexes);
            Assert.Equal(2, record.BatchSize);
            Assert.Equal(1, record.MaxAttemptedBatchIndex);   // 批0 失败 + 批1 成功，均被尝试
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_ProviderThrows_MarksFailed()
    {
        using var host = DataPortTestHost.Create(
            configure: services => services.AddScoped<IImportService>(_ => new ImportService(new ThrowOnReadProvider())));
        var filePath = await CreateTempDataFileAsync();

        try
        {
            // Provider 读取抛异常（记录已落库 Processing）→ 核心 ImportAsync 抛异常 → 记录更新为 Failed
            await Assert.ThrowsAsync<DataImportException>(
                () => host.TaskService.ImportAsync(filePath, new TestImportAdapter(), ct: TestContext.Current.CancellationToken));

            // 导入抛异常 → 无 BatchNo 可用；经 DataService 枚举回查（红线：断言不经裸 fsql.Select）
            var records = await host.DataService.EntitySelectAsync(predicate: null, ct: CancellationToken.None);
            var record = Assert.Single(records);

            Assert.NotNull(record);
            Assert.Equal("Failed", record!.Status);
            Assert.False(string.IsNullOrWhiteSpace(record.ErrorSummary));
            Assert.NotNull(record.EndTime);
            // V0.1.5（DP-04）：Failed 终态（MarkFailedAsync 异常路径）——三新列语义（BatchSize=首跑落库，失败集合/最大批次均 null）
            Assert.Equal(500, record.BatchSize);
            Assert.Null(record.FailedBatchIndexes);
            Assert.Null(record.MaxAttemptedBatchIndex);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_PreviouslyFailedBatch_ReimportsSuccessfully()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await CreateTempDataFileAsync();

        try
        {
            // First import: ThrowingBatchAdapter causes PartiallySucceeded
            var firstResult = await host.TaskService.ImportAsync(filePath, new ThrowingBatchAdapter(),
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);
            var firstRecord = await host.TaskService.GetRecordByBatchNoAsync(firstResult.BatchNo, CancellationToken.None);
            Assert.Equal("PartiallySucceeded", firstRecord!.Status);
            Assert.Equal("[0]", firstRecord.FailedBatchIndexes);
            Assert.Equal(2, firstRecord.BatchSize);

            // V0.1.5（DP-04 方案 B）：二次导入同文件 → 失败批次重放（BatchSize 必须与首跑一致——重放一致性校验）
            // 批0（失败）重放、批1（已成功且已尝试）跳过 → 消费方零重复写
            var secondResult = await host.TaskService.ImportAsync(filePath, new TestImportAdapter(),
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);
            Assert.Equal(firstResult.RecordId, secondResult.RecordId); // Same record
            Assert.Equal(firstResult.BatchNo, secondResult.BatchNo);

            var record = await host.TaskService.GetRecordByBatchNoAsync(firstResult.BatchNo, CancellationToken.None);
            Assert.Equal("Succeeded", record!.Status);
            Assert.Equal(3, record.SuccessCount);
            Assert.Null(record.FailedBatchIndexes);   // 重放收敛 → 失败集合清空
            Assert.Equal(1, record.MaxAttemptedBatchIndex);  // max(首跑 1, 本次 1)
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public async Task GetRecordByBatchNoAsync_NotExists_ReturnsNull()
    {
        using var host = DataPortTestHost.Create();

        var result = await host.TaskService.GetRecordByBatchNoAsync("no-such-batch", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // V0.1.5（DP-04）T1-T4 + T9-T11：失败批次重放（definition 模式 onBatch 过滤）
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>T1：重导仅重放失败批次（单批）。首导 BatchSize=2 批0 抛错 → FailedBatchIndexes=="[0]"；
    /// 二次导入 → CalledBatchIndexes == {0}（批1 未触发写）、业务表 3 行、终态 Succeeded。</summary>
    [Fact]
    public async Task T1_Reimport_ReplaysOnlyFailedBatch()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(3);   // 行号 0,1,2 → BatchSize=2 时为批0={0,1},批1={2}
        var firstAdapter = new CountingBatchAdapter(0);

        try
        {
            var first = await host.TaskService.ImportAsync(filePath, firstAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            var firstRecord = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("PartiallySucceeded", firstRecord!.Status);
            Assert.Equal("[0]", firstRecord.FailedBatchIndexes);
            Assert.Equal(2, firstRecord.BatchSize);
            Assert.Equal(1, firstRecord.MaxAttemptedBatchIndex);   // 批0、批1 均被尝试（continue 模式）
            Assert.Single(firstAdapter.BusinessTable);              // 批1 写入 1 行（行号 2）；批0 抛错不入库
            Assert.Equal(2, firstAdapter.CalledRowGroups.Count);    // 首导两次写调用（批0 抛错 + 批1 成功）

            // 二次导入：仅重放失败批（批0），已成功批（批1）跳过 → 零重复写 + 失败收敛
            var secondAdapter = new CountingBatchAdapter();
            var second = await host.TaskService.ImportAsync(filePath, secondAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            Assert.Equal(first.BatchNo, second.BatchNo);
            Assert.Single(secondAdapter.CalledRowGroups);   // 仅批0 触发 OnBatchWrite（批1 被重放过滤跳过）
            Assert.Equal(new[] { 0 }, secondAdapter.CalledBatchIndexes(2));   // 唯一写调用 = 批0

            var record = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("Succeeded", record!.Status);
            Assert.Equal(3, record.SuccessCount);
            Assert.Null(record.FailedBatchIndexes);
            Assert.Equal(1, record.MaxAttemptedBatchIndex);
            // 业务表合流：首导 1 行 + 重放 2 行 = 3 行（批1 未重复写）
            Assert.Equal(3, firstAdapter.BusinessTable.Count + secondAdapter.BusinessTable.Count);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    /// <summary>T2：重导消费方写库零重复——业务表总行数 = 3（首导 1 行 [行2] + 重放 2 行 [行0,行1]）。
    /// 若全量重导则 4 行（行2 二次写）；批1 未再写（幂等核心断言）。</summary>
    [Fact]
    public async Task T2_Reimport_NoDuplicateConsumerWrites()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(3);
        var firstAdapter = new CountingBatchAdapter(0);

        try
        {
            await host.TaskService.ImportAsync(filePath, firstAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            // 首导业务表：仅批1 的 1 行（行号 2）；批0 抛错不入库
            Assert.Single(firstAdapter.BusinessTable);
            Assert.Equal(2, firstAdapter.BusinessTable[0].RowNo);

            var secondAdapter = new CountingBatchAdapter();
            await host.TaskService.ImportAsync(filePath, secondAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            // 业务表总行数 = 3（追加批0 重放的 2 行）；批1 内容（行号2）仅出现一次 → 零重复写
            var merged = firstAdapter.BusinessTable.Concat(secondAdapter.BusinessTable).ToList();
            Assert.Equal(3, merged.Count);
            Assert.Equal(3, merged.Select(r => r.RowNo).Distinct().Count());   // 行号 {0,1,2} 各一
            Assert.Single(merged, r => r.RowNo == 2);   // 批1 未二次写库（若全量重导则 2 条行号2）
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    /// <summary>T3：多失败批次重放。BatchSize=1（4 行=4 批）批0/批2 抛错 → FailedBatchIndexes=="[0,2]"、
    /// MaxAttemptedBatchIndex==3；重导 → CalledBatchIndexes == {0,2}、业务表 4 行（批1/批3 首导 + 批0/批2 重放）、终态 Succeeded。</summary>
    [Fact]
    public async Task T3_Reimport_MultipleFailedBatches()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(4);   // 行号 0..3，BatchSize=1 → 4 批
        var firstAdapter = new CountingBatchAdapter(0, 2);

        try
        {
            var first = await host.TaskService.ImportAsync(filePath, firstAdapter,
                new ImportBatchOptions(BatchSize: 1, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            var firstRecord = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("PartiallySucceeded", firstRecord!.Status);
            Assert.Equal("[0,2]", firstRecord.FailedBatchIndexes);
            Assert.Equal(1, firstRecord.BatchSize);
            Assert.Equal(3, firstRecord.MaxAttemptedBatchIndex);
            Assert.Equal(2, firstAdapter.BusinessTable.Count);   // 批1、批3 写入（行号 1,3）
            Assert.Equal(new[] { 1, 3 }, firstAdapter.BusinessTable.Select(r => r.RowNo).OrderBy(x => x).ToArray());

            var secondAdapter = new CountingBatchAdapter();
            var second = await host.TaskService.ImportAsync(filePath, secondAdapter,
                new ImportBatchOptions(BatchSize: 1, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            Assert.Equal(first.BatchNo, second.BatchNo);
            Assert.Equal(new[] { 0, 2 }, secondAdapter.CalledBatchIndexes(1));   // 仅失败批重放，批1/批3 跳过

            var record = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("Succeeded", record!.Status);
            Assert.Null(record.FailedBatchIndexes);
            // 业务表合流：首导 [1,3] + 重放 [0,2] = 4 行无重复
            var merged = firstAdapter.BusinessTable.Concat(secondAdapter.BusinessTable).ToList();
            Assert.Equal(4, merged.Count);
            Assert.Equal(4, merged.Select(r => r.RowNo).Distinct().Count());
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    /// <summary>T4(a)：兼容旧记录——FailedBatchIndexes 手工置 null（模拟 V0.1.4 旧记录）→ 重导全量（CalledBatchIndexes == {0,1}）。</summary>
    [Fact]
    public async Task T4a_Reimport_LegacyPartiallySucceeded_FullReimport()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(3);

        try
        {
            var first = await host.TaskService.ImportAsync(filePath, new CountingBatchAdapter(0),
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            // 模拟旧记录：把 FailedBatchIndexes 清空（三新列在 V0.1.4 不存在）→ 重导按全量
            var record = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            record!.FailedBatchIndexes = null;
            await host.DataService.EntityUpdateAsync(record, ct: CancellationToken.None);

            var secondAdapter = new CountingBatchAdapter();
            var second = await host.TaskService.ImportAsync(filePath, secondAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            Assert.Equal(first.BatchNo, second.BatchNo);
            Assert.Equal(new[] { 0, 1 }, secondAdapter.CalledBatchIndexes(2));   // 全量重导（旧记录兼容，等效现状）
            var final = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("Succeeded", final!.Status);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    /// <summary>T4(b)：Failed 记录重导 → 全量（README 既有语义不回归）。</summary>
    [Fact]
    public async Task T4b_Reimport_FailedRecord_FullReimport()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(3);

        try
        {
            // 先正常导入 → 记录为 Succeeded（拿到 FileHash/记录），再手工改为 Failed 模拟"已失败批次"
            var first = await host.TaskService.ImportAsync(filePath, new TestImportAdapter(),
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);
            var record = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            record!.Status = "Failed";
            await host.DataService.EntityUpdateAsync(record, ct: CancellationToken.None);

            // Failed → else 分支（ResetToProcessingAsync 不清三列）→ 全量重导
            var secondAdapter = new CountingBatchAdapter();
            var second = await host.TaskService.ImportAsync(filePath, secondAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            Assert.Equal(first.BatchNo, second.BatchNo);
            Assert.Equal(new[] { 0, 1 }, secondAdapter.CalledBatchIndexes(2));   // Failed 全量（非重放过滤）
            var final = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("Succeeded", final!.Status);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    /// <summary>T4(c)：重导仍有失败 → 终态 PartiallySucceeded 且 FailedBatchIndexes 覆盖为新集合（多轮收敛）。
    /// 首导失败集合 [0,2] → 重导仅批2 再失败 → 新集合 [2]。</summary>
    [Fact]
    public async Task T4c_Reimport_StillFailing_NewFailedSet()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(4);

        try
        {
            var first = await host.TaskService.ImportAsync(filePath, new CountingBatchAdapter(0, 2),
                new ImportBatchOptions(BatchSize: 1, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);
            var firstRecord = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("[0,2]", firstRecord!.FailedBatchIndexes);

            // 重导：批0 重放成功、批2 重放仍失败 → 新失败集合 [2]（覆盖旧集合）
            var second = await host.TaskService.ImportAsync(filePath, new CountingBatchAdapter(1),
                new ImportBatchOptions(BatchSize: 1, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            Assert.Equal(first.BatchNo, second.BatchNo);
            var record = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("PartiallySucceeded", record!.Status);
            Assert.Equal("[2]", record.FailedBatchIndexes);   // 覆盖为新集合（多轮收敛路径）
            Assert.Equal(1, record.BatchSize);
            Assert.Equal(3, record.MaxAttemptedBatchIndex);   // max(首跑 3, 本次重放仍尝试到批3 被跳过时的闭包跟踪 3)
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    /// <summary>T9：StopOnBatchFailure 早停重放（P1-3）。首导 StopOnBatchFailure=true、批0 抛错 → 批1 未尝试
    /// （MaxAttemptedBatchIndex==0）；二次导入 → CalledBatchIndexes == {0,1}（未尝试尾部批1 纳入重放）、业务表 3 行（零丢失）、终态 Succeeded。</summary>
    [Fact]
    public async Task T9_Reimport_EarlyStoppedTailIncluded()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(3);   // 行号 0,1,2 → BatchSize=2 批0={0,1},批1={2}

        try
        {
            var firstAdapter = new CountingBatchAdapter(0);
            var first = await host.TaskService.ImportAsync(filePath, firstAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: true), ct: TestContext.Current.CancellationToken);

            var firstRecord = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("PartiallySucceeded", firstRecord!.Status);
            Assert.Equal("[0]", firstRecord.FailedBatchIndexes);
            Assert.Equal(0, firstRecord.MaxAttemptedBatchIndex);   // 早停：批1 从未被尝试（max=中断批次 0）
            Assert.Empty(firstAdapter.BusinessTable);               // 批0 抛错不入库、批1 未执行

            // 二次导入（continue）：批0 ∈ 失败集合重放 + 批1 ∈ 未尝试尾部（>= MaxAttempted+1）重放 → 零丢失
            var secondAdapter = new CountingBatchAdapter();
            await host.TaskService.ImportAsync(filePath, secondAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            Assert.Equal(new[] { 0, 1 }, secondAdapter.CalledBatchIndexes(2));   // 尾部批1 纳入重放
            Assert.Equal(3, secondAdapter.BusinessTable.Count);                 // 3 行全量落库（零丢失）
            Assert.Equal(new[] { 0, 1, 2 }, secondAdapter.BusinessTable.Select(r => r.RowNo).OrderBy(x => x).ToArray());

            var record = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("Succeeded", record!.Status);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    /// <summary>T10：BatchSize 不一致拒绝（P1-3）。首导 BatchSize=2 → 记录 BatchSize==2；
    /// 二次导入传 BatchSize=3 → 抛 DataImportException（重放 BatchSize 与首跑不一致）、记录不被重置（仍 PartiallySucceeded + 三列原值）。</summary>
    [Fact]
    public async Task T10_Reimport_BatchSizeMismatch_Rejects()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(3);

        try
        {
            var first = await host.TaskService.ImportAsync(filePath, new CountingBatchAdapter(0),
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            // 二次导入 BatchSize=3 ≠ 首跑 2 → 拒绝重放
            var ex = await Assert.ThrowsAsync<DataImportException>(() => host.TaskService.ImportAsync(
                filePath, new CountingBatchAdapter(),
                new ImportBatchOptions(BatchSize: 3, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken));
            Assert.Contains("BatchSize", ex.Message);
            Assert.Contains("不一致", ex.Message);

            // 记录不被重置：仍 PartiallySucceeded + 三新列原值（可再尝试）
            var record = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("PartiallySucceeded", record!.Status);
            Assert.Equal("[0]", record.FailedBatchIndexes);
            Assert.Equal(2, record.BatchSize);
            Assert.Equal(1, record.MaxAttemptedBatchIndex);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    /// <summary>T11：FailedBatchIndexes 解析失败降级全量（P1-3）。手工把记录 FailedBatchIndexes 改为非法 JSON（"[0,2"）
    /// → 二次导入 → LogWarning + 降级全量重导（CalledBatchIndexes=={0,1}、业务表全量、终态 Succeeded，不抛）。</summary>
    [Fact]
    public async Task T11_Reimport_CorruptFailedIndexes_DegradesToFull()
    {
        using var host = DataPortTestHost.Create();
        var filePath = await ImportServiceTestData.CreateTempDataFileAsync(3);

        try
        {
            var first = await host.TaskService.ImportAsync(filePath, new CountingBatchAdapter(0),
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            // 模拟脏数据：FailedBatchIndexes 非法 JSON
            var record = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            record!.FailedBatchIndexes = "[0,2";
            await host.DataService.EntityUpdateAsync(record, ct: CancellationToken.None);

            // 解析失败 → 降级全量重导（不抛）
            var secondAdapter = new CountingBatchAdapter();
            var second = await host.TaskService.ImportAsync(filePath, secondAdapter,
                new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false), ct: TestContext.Current.CancellationToken);

            Assert.Equal(first.BatchNo, second.BatchNo);
            Assert.Equal(new[] { 0, 1 }, secondAdapter.CalledBatchIndexes(2));   // 全量（脏数据降级路径）
            var final = await host.TaskService.GetRecordByBatchNoAsync(first.BatchNo, CancellationToken.None);
            Assert.Equal("Succeeded", final!.Status);
            Assert.Equal(3, secondAdapter.BusinessTable.Count);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    // ── 测试适配器 & Provider ────────────────────────────────────────────

    /// <summary>正常适配器——记录写库批次行数。列映射 "Name"/"Amount"。</summary>
    private sealed class TestImportAdapter : ImportAdapterBase<TestImportRow>
    {
        public override string DataSourceName => "测试导入";
        public override IReadOnlyDictionary<string, string> ColumnMapping { get; } =
            new Dictionary<string, string> { ["Name"] = "Name", ["Amount"] = "Amount" };

        public int TotalWrittenRows { get; private set; }

        protected override ImportRowAction OnValidate(
            int rowIndex, IReadOnlyDictionary<string, object?> row, TestImportRow entity)
            => entity.Amount > 0 ? ImportRowAction.Keep : ImportRowAction.Skip;

        public override Task OnBatchWrite(IReadOnlyList<TestImportRow> batch, CancellationToken ct)
        {
            TotalWrittenRows += batch.Count;
            return Task.CompletedTask;
        }
    }

    /// <summary>首批持久化抛错适配器——模拟消费方批次写库失败（StopOnBatchFailure=false 继续）。</summary>
    private sealed class ThrowingBatchAdapter : ImportAdapterBase<TestImportRow>
    {
        private int _batchIndex;

        public override string DataSourceName => "抛错导入";
        public override IReadOnlyDictionary<string, string> ColumnMapping { get; } =
            new Dictionary<string, string> { ["Name"] = "Name", ["Amount"] = "Amount" };

        public int BatchCount { get; private set; }

        protected override ImportRowAction OnValidate(
            int rowIndex, IReadOnlyDictionary<string, object?> row, TestImportRow entity)
            => entity.Amount > 0 ? ImportRowAction.Keep : ImportRowAction.Skip;

        public override Task OnBatchWrite(IReadOnlyList<TestImportRow> batch, CancellationToken ct)
        {
            Interlocked.Increment(ref _batchIndex);
            BatchCount = _batchIndex;
            if (_batchIndex == 1)
                throw new InvalidOperationException("首批持久化失败");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// V0.1.5 可观测计数适配器（方案 §3.4）——记录每次 OnBatchWrite 的行号组 + 业务表（模拟消费方写库）。
    /// <para>生产定义模式 onBatch 闭包直接调用 <see cref="OnBatchWrite"/>（public virtual）——跳过批次不触发调用，
    /// 因此 <see cref="CalledRowGroups"/> 精确等于"实际触发写库的批次"。批次号由行号组推导：
    /// <c>min(RowNo) / BatchSize</c>（同批行号连续，min/批距 = 批次索引，零包装器内部计数耦合）。</para>
    /// </summary>
    internal sealed class CountingBatchAdapter : ImportAdapterBase<TestImportRow>
    {
        private readonly HashSet<int> _failOnCall = new();

        public CountingBatchAdapter(params int[] failOnCall)
        {
            foreach (var c in failOnCall) _failOnCall.Add(c);
        }

        public override string DataSourceName => "计数导入";
        public override IReadOnlyDictionary<string, string> ColumnMapping { get; } =
            new Dictionary<string, string> { ["RowNo"] = "RowNo", ["Name"] = "Name", ["Amount"] = "Amount" };

        /// <summary>每次 OnBatchWrite 的行号组（按调用顺序）。</summary>
        public List<List<int>> CalledRowGroups { get; } = new();

        /// <summary>模拟消费方业务表——仅成功批次写入（抛错批次不入库，对齐"批次失败 = 消费方写库未生效"）。</summary>
        public List<TestImportRow> BusinessTable { get; } = new();

        protected override ImportRowAction OnValidate(
            int rowIndex, IReadOnlyDictionary<string, object?> row, TestImportRow entity)
            => entity.Amount > 0 ? ImportRowAction.Keep : ImportRowAction.Skip;

        public override Task OnBatchWrite(IReadOnlyList<TestImportRow> batch, CancellationToken ct)
        {
            var callIndex = CalledRowGroups.Count;
            CalledRowGroups.Add(batch.Select(r => r.RowNo).ToList());
            if (_failOnCall.Contains(callIndex))
                throw new InvalidOperationException($"模拟批次写库失败（第 {callIndex} 次调用）");
            foreach (var r in batch) BusinessTable.Add(r);
            return Task.CompletedTask;
        }

        /// <summary>推导实际触发写库的批次索引集合（min RowNo / BatchSize）。</summary>
        public int[] CalledBatchIndexes(int batchSize)
            => CalledRowGroups.Select(g => g.Count == 0 ? -1 : g.Min() / batchSize).ToArray();
    }

    /// <summary>读取即抛异常的 Provider——模拟文件读取失败（验证 Failed 状态更新路径）。</summary>
    private sealed class ThrowOnReadProvider : IImportProvider
    {
        public string Name => "throw";

        public async IAsyncEnumerable<ImportRow> ReadAsync(
            string filePath, ImportReadOptions readOptions,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return ThrowFor(filePath);
        }

        private static ImportRow ThrowFor(string filePath)
            => throw new DataImportException($"模拟读取失败:{filePath}");
    }

    /// <summary>测试导入目标实体（消费方业务实体替身）——含 RowNo 供批次推导。</summary>
    internal sealed class TestImportRow
    {
        public int RowNo { get; set; }
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
    }
}