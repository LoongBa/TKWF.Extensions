using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Options;
using TKW.Framework.Utility.DataPort;

namespace TKWF.Ext.DataPort
{
    /// <summary>
    /// 数据导入任务服务实现——封装导入执行 + 批次记录落库 + FileHash 幂等检查 + 状态跟踪。
    /// <para>消费方自管业务数据持久化（adapter.OnBatchWrite 钩子）；本服务仅记录批次元数据。</para>
    /// </summary>
    internal sealed class DataImportTaskService : IDataImportTaskService
    {
        private readonly IImportService _importService;
        private readonly IFreeSql _freeSql;
        private readonly DataPortOptions _options;

        public DataImportTaskService(
            IImportService importService,
            IFreeSql freeSql,
            IOptions<DataPortOptions> options)
        {
            _importService = importService ?? throw new ArgumentNullException(nameof(importService));
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public async Task<DataImportTaskResult> ImportAsync<T>(
            string filePath,
            ImportAdapterBase<T> adapter,
            ImportBatchOptions? batchOptions = null,
            CancellationToken ct = default) where T : new()
        {
            ArgumentNullException.ThrowIfNull(filePath);
            ArgumentNullException.ThrowIfNull(adapter);

            // 1. 计算文件哈希（幂等检查）
            var fileHash = await ComputeFileHashAsync(filePath, ct);

            // 2. 幂等检查：同文件已导入 → 返回已有批次
            var existing = await _freeSql.Select<DataImportRecordEntity>()
                .Where(r => r.FileHash == fileHash)
                .FirstAsync(ct);

            if (existing != null)
            {
                // 已成功或处理中的批次 → 拒绝重复导入
                if (existing.Status is "Processing" or "Succeeded")
                {
                    return new DataImportTaskResult(existing.Id, existing.BatchNo, ImportResult.Empty);
                }
                // 已失败的批次 → 允许重新导入（重置记录为 Processing 后重跑）
                await _freeSql.Ado.ExecuteNonQueryAsync(
                    "UPDATE DataImportRecord SET Status = 'Processing', StartTime = @startTime, EndTime = NULL, ErrorSummary = NULL, SuccessCount = 0, FailedCount = 0, BatchFailureCount = 0, UpdateTime = @updateTime WHERE Id = @id",
                    new { startTime = DateTime.UtcNow, updateTime = DateTime.UtcNow, id = existing.Id });

                return await ExecuteImportAsync(existing, filePath, adapter, batchOptions, ct);
            }

            // 3. 新记录：创建 Processing 状态
            var batchNo = Guid.NewGuid().ToString("N");
            DataImportRecordEntity record;
            try
            {
                record = new DataImportRecordEntity
                {
                    BatchNo = batchNo,
                    FileHash = fileHash,
                    FileName = Path.GetFileName(filePath),
                    ProviderName = adapter.GetType().Name,
                    Status = "Processing",
                    StartTime = DateTime.UtcNow,
                    CreateTime = DateTime.UtcNow,
                    UpdateTime = DateTime.UtcNow
                };

                await _freeSql.Insert(record).ExecuteAffrowsAsync(ct);
                record = await _freeSql.Select<DataImportRecordEntity>()
                    .Where(r => r.BatchNo == batchNo)
                    .FirstAsync(ct);
            }
            catch (Exception)
            {
                // Race condition: another concurrent import inserted the same FileHash
                record = await _freeSql.Select<DataImportRecordEntity>()
                    .Where(r => r.FileHash == fileHash)
                    .FirstAsync(ct);

                if (record == null)
                    throw; // Not a race condition — re-throw original exception

                // Another import already created this record — return it (idempotent)
                return new DataImportTaskResult(record.Id, record.BatchNo, ImportResult.Empty);
            }

            return await ExecuteImportAsync(record!, filePath, adapter, batchOptions, ct);
        }

        /// <inheritdoc />
        public async Task<DataImportRecordEntity?> GetRecordByBatchNoAsync(
            string batchNo, CancellationToken ct = default)
        {
            return await _freeSql.Select<DataImportRecordEntity>()
                .Where(r => r.BatchNo == batchNo)
                .FirstAsync(ct);
        }

        /// <summary>
        /// 调用核心导入服务，并据结果更新记录状态。
        /// <para>BatchFailures &gt; 0 → PartiallySucceeded；核心异常 → Failed + 错误摘要；其余 → Succeeded。</para>
        /// </summary>
        private async Task<DataImportTaskResult> ExecuteImportAsync<T>(
            DataImportRecordEntity record,
            string filePath,
            ImportAdapterBase<T> adapter,
            ImportBatchOptions? batchOptions,
            CancellationToken ct) where T : new()
        {
            var effectiveBatchOptions = batchOptions
                ?? new ImportBatchOptions(_options.DefaultBatchSize, _options.StopOnBatchFailure);

            try
            {
                var result = await _importService.ImportAsync(filePath, adapter, effectiveBatchOptions, ct);

                // 更新记录状态（原始 SQL：时间列以 UTC DateTime 落库，避免 Provider 类型映射差异）
                var status = result.BatchFailures.Count > 0 ? "PartiallySucceeded" : "Succeeded";
                var errorSummary = result.BatchFailures.Count > 0
                    ? string.Join("; ", result.BatchFailures.Take(5).Select(b => $"批次{b.BatchIndex}: {b.Exception.Message}"))
                    : null;

                await _freeSql.Ado.ExecuteNonQueryAsync(
                    "UPDATE DataImportRecord SET Status = @status, SuccessCount = @successCount, FailedCount = @failedCount, BatchFailureCount = @batchFailureCount, EndTime = @endTime, ErrorSummary = @errorSummary, UpdateTime = @updateTime WHERE Id = @id",
                    new { status, successCount = result.SuccessCount, failedCount = result.FailedCount, batchFailureCount = result.BatchFailures.Count, endTime = DateTime.UtcNow, errorSummary, updateTime = DateTime.UtcNow, id = record.Id });

                return new DataImportTaskResult(record.Id, record.BatchNo, result);
            }
            catch (Exception ex)
            {
                // Best-effort: update record to Failed status; if DB update fails, swallow to preserve original exception
                try
                {
                    await _freeSql.Ado.ExecuteNonQueryAsync(
                        "UPDATE DataImportRecord SET Status = 'Failed', EndTime = @endTime, ErrorSummary = @errorSummary, UpdateTime = @updateTime WHERE Id = @id",
                        new { endTime = DateTime.UtcNow, errorSummary = ex.Message, updateTime = DateTime.UtcNow, id = record.Id });
                }
                catch
                {
                    // DB update failed — swallow to avoid masking the original import exception
                }

                throw;
            }
        }

        private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct)
        {
            using var sha256 = SHA256.Create();
            await using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}