using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.DataPort;

namespace TKWF.Ext.DataPort
{
    /// <summary>
    /// 数据导入任务服务实现——封装导入执行 + 批次记录落库 + FileHash 幂等检查 + 状态跟踪。
    /// <para>消费方自管业务数据持久化（adapter.OnBatchWrite 钩子）；本服务仅记录批次元数据。</para>
    /// <para>数据访问红线整改（2026-09-07）：经 <see cref="DataImportRecordEntityDataService"/>（SG1 DataService）
    /// 委托持久化——不直接注入 IFreeSql；原 3 处 raw SQL（重置/更新状态/标记失败）改为 DataService 业务方法。</para>
    /// <para>V4.10.53（领域自治根治，ADR90，正确路线）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c>
    /// 获取用户上下文（<b>IDomainUser 永不注册 DI</b>，D01；旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败——
    /// v0.3.3 同根缺陷）；<c>[DiContractIgnore]</c> 豁免 DI001（AddConstructibleService 运行时手写注册）；
    /// 注册形态改 <c>AddConstructibleService&lt;IDataImportTaskService, DataImportTaskService&gt;</c>
    /// （接口可构造守卫工厂 + 实现类 throw-factory，消费方经 <c>User.Use&lt;IDataImportTaskService&gt;()</c> 解析）。
    /// ctor 保留 <see cref="IImportService"/>（框架核心运行库接线型，守卫工厂 ActivatorUtilities 从 DI 解析）。</para>
    /// <para>V0.1.5（DP-04/DP-05）：PartiallySucceeded 幂等语义 = 失败批次重放（definition 模式 onBatch 过滤——仅重放
    /// "失败 ∪ 未尝试尾部"批次，消费方已成功批次零重复写）；Processing 超时治理（心跳 + CAS 恢复门面，见
    /// <see cref="IStaleImportRecoveryService"/>）。<b>P0-1 机制落地形态（方案 B 反射降级）</b>：框架
    /// <c>BuildImportDefinition()</c> 仍为 internal（转达框架组公开中）——本地经反射提取 internal 定义接入
    /// definition 模式 onBatch 重载（核心零改动）；反射失败 → 警告并安全降级全量重导（输入同文件不丢数据，
    /// 仅失去重放过滤语义）。反射脆弱性已披露：核心运行库内部实现（字段/签名）演进可能破坏反射提取，仅作应急通道、
    /// 不作为长期方案（后续框架公开后移除反射改直调）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class DataImportTaskService : DomainServiceBase, IDataImportTaskService
    {
        private readonly IImportService _importService;
        private DataImportRecordEntityDataService? _dataService;
        private readonly DataPortOptions _options;
        private readonly ILogger<DataImportTaskService> _logger;

        private DataImportRecordEntityDataService DataService => _dataService ??= User.Use<DataImportRecordEntityDataService>();

        public DataImportTaskService(
            IDomainUser user,
            IImportService importService,
            IOptions<DataPortOptions> options,
            ILogger<DataImportTaskService> logger)
            : base(user)
        {
            _importService = importService ?? throw new ArgumentNullException(nameof(importService));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            var effectiveBatchOptions = batchOptions
                ?? new ImportBatchOptions(_options.DefaultBatchSize, _options.StopOnBatchFailure);

            // 1. 计算文件哈希（幂等检查）
            var fileHash = await ComputeFileHashAsync(filePath, ct);

            // 2. 幂等检查：同文件已导入 → 返回已有批次
            var existing = await DataService.GetByFileHashAsync(fileHash, ct);

            if (existing != null)
            {
                // 已成功或处理中的批次 → 拒绝重复导入
                if (existing.Status is "Processing" or "Succeeded")
                {
                    return new DataImportTaskResult(existing.Id, existing.BatchNo, ImportResult.Empty);
                }

                // V0.1.5（DP-04 方案 B）：PartiallySucceeded + 失败批次索引非空 → 失败批次重放（批次级幂等）
                // 旧记录（三新列均 null）→ 落 if 之外走全量重导（等效现状，升级零破坏）
                if (existing.Status is "PartiallySucceeded" && !string.IsNullOrWhiteSpace(existing.FailedBatchIndexes))
                {
                    return await ReplayPartiallySucceededAsync(existing, filePath, adapter, effectiveBatchOptions, ct);
                }

                // 已失败的批次 → 允许重新导入（重置记录为 Processing 后重跑）
                // StartTime 保留首次导入时间（CanUpdate=false 语义优于原 raw SQL 覆盖）
                await DataService.ResetToProcessingAsync(existing.Id, DateTime.UtcNow, ct);

                return await ExecuteImportAsync(existing, filePath, adapter, effectiveBatchOptions, ct: ct);
            }

            // 3. 新记录：创建 Processing 状态（BatchSize 首跑落库——重放一致性校验依据，P0-2 ①）
            var batchNo = Guid.NewGuid().ToString("N");
            DataImportRecordEntity? record;
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
                    UpdateTime = DateTime.UtcNow,
                    BatchSize = effectiveBatchOptions.BatchSize
                };

                await DataService.CreateAsync(record, ct);
                record = await DataService.GetByBatchNoAsync(batchNo, ct);
                if (record is null) throw new InvalidOperationException("导入批次创建后查询失败");
            }
            catch (Exception)
            {
                // Race condition: another concurrent import inserted the same FileHash
                record = await DataService.GetByFileHashAsync(fileHash, ct);

                if (record == null)
                    throw; // Not a race condition — re-throw original exception

                // Another import already created this record — return it (idempotent)
                return new DataImportTaskResult(record.Id, record.BatchNo, ImportResult.Empty);
            }

            return await ExecuteImportAsync(record!, filePath, adapter, effectiveBatchOptions, ct: ct);
        }

        /// <inheritdoc />
        public async Task<DataImportRecordEntity?> GetRecordByBatchNoAsync(
            string batchNo, CancellationToken ct = default)
        {
            return await DataService.GetByBatchNoAsync(batchNo, ct);
        }

        /// <summary>
        /// PartiallySucceeded 失败批次重放（DP-04 方案 B）——仅重放 "失败 ∪ 未尝试尾部" 批次，消费方零重复写、零数据丢失。
        /// </summary>
        private async Task<DataImportTaskResult> ReplayPartiallySucceededAsync<T>(
            DataImportRecordEntity existing,
            string filePath,
            ImportAdapterBase<T> adapter,
            ImportBatchOptions effectiveBatchOptions,
            CancellationToken ct) where T : new()
        {
            // ① P0-2 ① BatchSize 一致性校验：首跑 BatchSize 有值且与本次解析批距不等 → 拒绝重放（记录不被重置，保持可再尝试）
            if (existing.BatchSize is int recorded && effectiveBatchOptions.BatchSize != recorded)
                throw new DataImportException($"重放 BatchSize {effectiveBatchOptions.BatchSize} 与首跑 {recorded} 不一致，拒绝重放");

            // ② BuildReplayFilter 构造重放过滤器（P0-2 ② 统一判据：失败 ∪ 未尝试尾部纳集）
            var replayFilter = BuildReplayFilter(existing);

            // ③ ResetToProcessingAsync（不清三列）→ 重放执行（批距 = 校验后值）
            // 重导崩溃 → 停留 Processing（无 MarkFailed 包裹——计划 §3.3 ⑤ 语义）：DP-05 超时治理扫描将恢复为 Failed（§4），
            // 随后 Failed 全量重导（双写警示见 README §三.5 恢复与重导）
            await DataService.ResetToProcessingAsync(existing.Id, DateTime.UtcNow, ct);
            var replayResult = await ExecuteCoreImportAsync(
                filePath, adapter, replayFilter, effectiveBatchOptions, existing.Id, existing.MaxAttemptedBatchIndex, ct);
            return new DataImportTaskResult(existing.Id, existing.BatchNo, replayResult);
        }

        /// <summary>
        /// 调用核心导入服务，并据结果更新记录状态（全量/重放统一执行路径）。
        /// <para>BatchFailures &gt; 0 → PartiallySucceeded；核心异常 → Failed + 错误摘要；其余 → Succeeded。</para>
        /// </summary>
        private async Task<DataImportTaskResult> ExecuteImportAsync<T>(
            DataImportRecordEntity record,
            string filePath,
            ImportAdapterBase<T> adapter,
            ImportBatchOptions effectiveBatchOptions,
            ReplayFilter? replayFilter = null,
            CancellationToken ct = default) where T : new()
        {
            try
            {
                var result = await ExecuteCoreImportAsync(
                    filePath, adapter, replayFilter, effectiveBatchOptions, record.Id, record.MaxAttemptedBatchIndex, ct);

                return new DataImportTaskResult(record.Id, record.BatchNo, result);
            }
            catch (Exception ex)
            {
                // 重导中途崩溃（managed 异常）→ 停留 Processing 或 best-effort 标 Failed：
                // DP-05 超时治理接管（§3.3 ⑤/⑥ 边界）——MarkFailed 后 Failed 全量重导（双写警示见 README §三.5）
                try
                {
                    await DataService.MarkFailedAsync(
                        record.Id, DateTime.UtcNow, ex.Message, DateTime.UtcNow, ct);
                }
                catch
                {
                    // DB update failed — swallow to avoid masking the original import exception
                }

                throw;
            }
        }

        /// <summary>
        /// 统一核心执行路径（DP-04 §3.3 ④）——definition 模式 onBatch 回调内完成 "重放过滤 + 心跳 + 已尝试上限跟踪" 三职责。
        /// <para><b>P0-1 方案 B（反射降级）</b>：框架 <c>BuildImportDefinition</c> 仍 internal（未公开，已转达框架组）——本地
        /// 经反射提取 internal 定义接入 public definition 重载；反射提取失败 → LogWarning + 安全降级全量重导
        /// （adapter 模板方法模式，无重放过滤/心跳/已尝试跟踪——输入同文件不丢数据，仅失去批次级幂等保住语义）。</para>
        /// </summary>
        private async Task<ImportResult> ExecuteCoreImportAsync<T>(
            string filePath,
            ImportAdapterBase<T> adapter,
            ReplayFilter? replayFilter,                 // null = 全量重放（新建/Failed/旧记录兼容）
            ImportBatchOptions batchOptions,
            long recordId,
            int? priorMaxAttemptedBatchIndex,           // 既有 MaxAttemptedBatchIndex（重放/全量路径取 max 防低估首跑覆盖，P0-2 ②）
            CancellationToken ct) where T : new()
        {
            var definition = TryBuildImportDefinition(adapter);

            if (definition is null)
            {
                // ⚠️ P0-1 降级（方案 B 安全降级全量重导）：反射提取内部 definition 失败，退化到框架既有 adapter 模板方法
                // 重载（其内部自身已封装 BuildImportDefinition 调用）——语义与 V0.1.4 全量重导完全一致（含掉线后状态更新）。
                // 声明：框架 `BuildImportDefinition` 仍 internal（refs 4.10.117 实证）→ 反射提取为应急降级（脆弱性已披露，见方案 §3.3 ①）。
                _logger.LogWarning("DataPort: 反射提取 BuildImportDefinition 失败，降级 adapter 模板方法全量重导");
                var degraded = await _importService.ImportAsync(filePath, adapter, batchOptions, ct);
                await PersistResultStateAsync(recordId, degraded, priorMaxAttemptedBatchIndex, maxAttempted: null, ct);
                return degraded;
            }

            var maxAttempted = -1;                              // 已尝试最大批次索引（onBatch 跟踪）
            var lastHeartbeat = DateTime.UtcNow;

            var result = await _importService.ImportAsync(filePath, definition,
                async (batchIndex, startRowIndex, batch, ct2) =>
                {
                    if (maxAttempted < batchIndex) maxAttempted = batchIndex;

                    // ⚠️ 心跳（DP-05 P1-2，时间驱动）：距上次 touch ≥ HeartbeatIntervalMinutes → CAS 刷新 UpdateTime
                    // （仅 Processing 态更新——防覆写终态）；失败不终止导入（瞬时 DB 故障不杀合法导入）
                    if (_options.HeartbeatIntervalMinutes > 0 &&
                        (DateTime.UtcNow - lastHeartbeat).TotalMinutes >= _options.HeartbeatIntervalMinutes)
                    {
                        try
                        {
                            await DataService.TouchProcessingAsync(recordId, DateTime.UtcNow, ct2);
                            lastHeartbeat = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "DataPort 心跳 touch 失败（不终止导入）");
                        }
                    }

                    // 重放过滤（P0-2 ② 统一判据）：跳过"已成功且已尝试"批次 = 消费方零写库
                    if (replayFilter is not null && !replayFilter.ShouldReplay(batchIndex))
                        return;

                    // OnBatchWrite 为 public virtual——扩展可直接调用（definition 模式回调经核心调度）
                    await adapter.OnBatchWrite(batch, ct2);
                },
                batchOptions, ct);

            await PersistResultStateAsync(recordId, result, priorMaxAttemptedBatchIndex, maxAttempted, ct);
            return result;
        }

        /// <summary>重放判定状态落库（FailedBatchIndexes/MaxAttemptedBatchIndex——取 max(既有, 本次)，防重导早停低估首跑覆盖）。</summary>
        private async Task PersistResultStateAsync(
            long recordId,
            ImportResult result,
            int? priorMaxAttemptedBatchIndex,
            int? maxAttempted,
            CancellationToken ct)
        {
            var status = result.BatchFailures.Count > 0 ? "PartiallySucceeded" : "Succeeded";
            var errorSummary = result.BatchFailures.Count > 0
                ? string.Join("; ", result.BatchFailures.Take(5).Select(b => $"批次{b.BatchIndex}: {b.Exception.Message}"))
                : null;
            string? failedBatchIndexes = result.BatchFailures.Count > 0
                ? JsonSerializer.Serialize(result.BatchFailures.Select(b => b.BatchIndex).Distinct().OrderBy(i => i).ToArray())
                : null;

            // max(既有, 本次)：防重导早停低估首跑覆盖（既有 = 重放前记录上的 MaxAttemptedBatchIndex，新纪录为 null → 即本次）
            var combined = Math.Max(priorMaxAttemptedBatchIndex ?? -1, maxAttempted ?? -1);
            int? combinedOrNull = combined >= 0 ? combined : null;   // MaxAttemptedBatchIndex 列语义：≥0 或 null

            await DataService.UpdateStatusAsync(
                recordId, status,
                result.SuccessCount, result.FailedCount, result.BatchFailures.Count,
                DateTime.UtcNow, errorSummary, DateTime.UtcNow,
                ct, failedBatchIndexes, combinedOrNull);
        }

        /// <summary>
        /// ⚠️ P0-1 降级：反射提取框架 internal <c>BuildImportDefinition</c>（方案 §3.3 ① 转达框架组公开的应急通道）。
        /// 成功 → 返回含消费方 SheetName/OnValidate/OnConvert 的 <see cref="ImportDefinition{T}"/>；失败 → null（调用方降级全量重导）。
        /// </summary>
        internal static ImportDefinition<T>? TryBuildImportDefinition<T>(ImportAdapterBase<T> adapter) where T : new()
        {
            try
            {
                var method = typeof(ImportAdapterBase<T>).GetMethod(
                    "BuildImportDefinition",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return method?.Invoke(adapter, null) as ImportDefinition<T>;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// P0-2 ② 统一重放判据（判定法，一条规则覆盖 continue/stop/Terminate 一切中断源）：
        /// <c>replay(batchIndex) = (batchIndex ∈ FailedBatchIndexes) || (batchIndex &gt; MaxAttemptedBatchIndex)</c>
        /// ——continue 模式 MaxAttempted=末批 → 尾部为空集 → 精确重放失败批次（零重复写）；
        /// 早停 模式 MaxAttempted=中断批次 → 未尝试尾部全部纳入重放（零数据丢失），已成功前缀仍跳过（零重复写）。
        /// </summary>
        internal sealed record ReplayFilter(HashSet<int>? FailedSet, int? ReplayFromBatchIndex)
        {
            /// <summary>全量重放（新建/Failed/旧记录/脏数据降级）——FailedSet 与 ReplayFrom 均 null 时全部批次应重放。</summary>
            public static ReplayFilter Full => new(null, null);

            public bool ShouldReplay(int batchIndex)
                => (FailedSet is null && ReplayFromBatchIndex is null)   // Full：全量重放
                   || (FailedSet is not null && FailedSet.Contains(batchIndex))
                   || (ReplayFromBatchIndex is int from && batchIndex >= from);
        }

        /// <summary>从既有记录构造重放过滤器：FailedBatchIndexes 空/解析失败 → 全量（旧记录兼容 / 脏数据降级）。</summary>
        internal ReplayFilter BuildReplayFilter(DataImportRecordEntity existing)
        {
            var failed = ParseFailedBatchIndexes(existing.FailedBatchIndexes);
            if (failed is null)
            {
                if (!string.IsNullOrWhiteSpace(existing.FailedBatchIndexes))
                    _logger.LogWarning($"DataPort: FailedBatchIndexes 解析失败（{existing.FailedBatchIndexes}），降级全量重导");
                return ReplayFilter.Full;
            }

            return new ReplayFilter(
                failed,
                existing.MaxAttemptedBatchIndex is int m && m >= 0 ? m + 1 : null);
        }

        /// <summary>JSON 数字数组字符串 → HashSet（如 "[0,2]" → {0,2}）；null/空白/非法 JSON → null（降级全量）。</summary>
        internal static HashSet<int>? ParseFailedBatchIndexes(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var arr = JsonSerializer.Deserialize<int[]>(json);
                return arr is null ? null : new HashSet<int>(arr);
            }
            catch (JsonException)
            {
                return null;
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