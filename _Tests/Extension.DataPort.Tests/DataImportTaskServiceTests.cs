using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Options;
using miniExcel = MiniExcelLibs;
using TKW.Framework.Utility.DataPort;
using TKW.Framework.Utility.DataPort.Providers.MiniExcel;

namespace TKWF.Ext.DataPort.Tests;

/// <summary>
/// DataImportTaskService 测试——使用 SQLite 内存库 + MiniExcel 真实读写引擎验证
/// 批次记录落库（Processing→Succeeded/Failed/PartiallySucceeded）+ FileHash 幂等 + 按 BatchNo 查询。
/// <para>内部类型经 <c>InternalsVisibleTo</c> 直接实例化（对齐 PrintTemplates 存储测试模式）。</para>
/// </summary>
public class DataImportTaskServiceTests
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（每次调用新连接 = 独立内存库）。</summary>
    private static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>同步 DataImportRecord 表（含 FileHash/BatchNo 唯一索引）。</summary>
    private static void SyncStructure(IFreeSql fsql)
        => fsql.CodeFirst.SyncStructure<DataImportRecordEntity>();

    /// <summary>创建含 3 行数据（A/B/C，金额均 &gt; 0）的临时 xlsx 文件。</summary>
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

    private static DataImportTaskService CreateTaskService(IFreeSql fsql)
    {
        var importService = new ImportService(new MiniExcelImportProvider());
        return new DataImportTaskService(importService, fsql, Options.Create(new DataPortOptions()));
    }

    [Fact]
    public async Task ImportAsync_NewFile_CreatesRecordWithSucceededStatus()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var service = CreateTaskService(fsql);
        var filePath = await CreateTempDataFileAsync();

        try
        {
            var result = await service.ImportAsync(filePath, new TestImportAdapter());

            Assert.True(result.RecordId > 0);
            Assert.False(string.IsNullOrWhiteSpace(result.BatchNo));
            Assert.Equal(3, result.ImportResult.SuccessCount);
            Assert.Empty(result.ImportResult.Failures);
            Assert.Empty(result.ImportResult.BatchFailures);

            var record = await fsql.Select<DataImportRecordEntity>()
                .Where(r => r.Id == result.RecordId)
                .FirstAsync();

            Assert.NotNull(record);
            Assert.Equal(result.BatchNo, record!.BatchNo);
            Assert.Equal("Succeeded", record.Status);
            Assert.Equal(3, record.SuccessCount);
            Assert.Equal(0, record.FailedCount);
            Assert.Equal(0, record.BatchFailureCount);
            Assert.Equal("TestImportAdapter", record.ProviderName);
            Assert.NotNull(record.EndTime);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_SameFileSecondTime_ReturnsExistingBatch()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var service = CreateTaskService(fsql);
        var filePath = await CreateTempDataFileAsync();

        try
        {
            var first = await service.ImportAsync(filePath, new TestImportAdapter());
            var second = await service.ImportAsync(filePath, new TestImportAdapter());

            // 幂等：同一文件（FileHash 相同）二次导入 → 拒绝/返回已有批次
            Assert.Equal(first.BatchNo, second.BatchNo);
            Assert.Equal(first.RecordId, second.RecordId);
            Assert.Same(ImportResult.Empty, second.ImportResult);

            // 数据库仅一条批次记录
            var count = fsql.Select<DataImportRecordEntity>().Count();
            Assert.Equal(1, count);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_FailedBatch_MarksPartiallySucceeded()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var service = CreateTaskService(fsql);
        var filePath = await CreateTempDataFileAsync();

        try
        {
            // BatchSize=2：首批（行 1-2）持久化抛错 → BatchFailure；末批（行 3）成功
            var adapter = new ThrowingBatchAdapter();
            var batchOptions = new ImportBatchOptions(BatchSize: 2, StopOnBatchFailure: false);

            var result = await service.ImportAsync(filePath, adapter, batchOptions);

            Assert.NotEmpty(result.ImportResult.BatchFailures);
            Assert.Equal(2, adapter.BatchCount);

            var record = await fsql.Select<DataImportRecordEntity>()
                .Where(r => r.Id == result.RecordId)
                .FirstAsync();

            Assert.NotNull(record);
            Assert.Equal("PartiallySucceeded", record!.Status);
            Assert.Equal(3, record.SuccessCount);
            Assert.Equal(1, record.BatchFailureCount);
            Assert.False(string.IsNullOrWhiteSpace(record.ErrorSummary));
            Assert.NotNull(record.EndTime);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ImportAsync_ProviderThrows_MarksFailed()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var filePath = await CreateTempDataFileAsync();

        try
        {
            // Provider 读取抛异常（记录已落库 Processing）→ 核心 ImportAsync 抛异常 → 记录更新为 Failed
            var importService = new ImportService(new ThrowOnReadProvider());
            var service = new DataImportTaskService(importService, fsql, Options.Create(new DataPortOptions()));

            await Assert.ThrowsAsync<DataImportException>(
                () => service.ImportAsync(filePath, new TestImportAdapter()));

            var record = await fsql.Select<DataImportRecordEntity>().FirstAsync();

            Assert.NotNull(record);
            Assert.Equal("Failed", record!.Status);
            Assert.False(string.IsNullOrWhiteSpace(record.ErrorSummary));
            Assert.NotNull(record.EndTime);
        }
        finally
        {
            System.IO.File.Delete(filePath);
        }
    }

    [Fact]
    public async Task GetRecordByBatchNoAsync_NotExists_ReturnsNull()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var service = CreateTaskService(fsql);

        var result = await service.GetRecordByBatchNoAsync("no-such-batch");

        Assert.Null(result);
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

    /// <summary>测试导入目标实体（消费方业务实体替身）。</summary>
    private sealed class TestImportRow
    {
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
    }
}