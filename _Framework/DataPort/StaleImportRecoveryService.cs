using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataPort
{
    /// <summary>
    /// Processing 超时恢复服务实现（DP-05 V0.1.5）。
    /// <list type="bullet">
    /// <item><b>超时判定</b>：<c>Status == "Processing" &amp;&amp; UpdateTime &lt; now - ProcessingTimeoutMinutes</c>（基于 UpdateTime——
    ///     创建时设置 + 导入心跳每间隔刷新，活跃导入天然跳过）；</item>
    /// <item><b>CAS 恢复（P1-1）</b>：逐记录 <see cref="DataImportRecordEntityDataService.MarkStaleFailedAsync"/>
    ///     ——条件更新 WHERE Id AND Status='Processing' AND UpdateTime&lt;阈值（弃用无条件 MarkFailedAsync 整实体读改写——
    ///     scan→mark 间隙并发终态写入会被覆写）；影响行数 = 1 → 恢复计数 +1；= 0 → 已被并发终态化/心跳刷新/他实例恢复 → 跳过；</item>
    /// <item><b>错误摘要语义</b>：恢复为 Failed 而非删除——保留元数据 + BatchNo 回滚钩子 + 重导入口（Failed 可全量重导）。
    ///     ⚠️ 恢复重导双写警示（P1-4）：崩溃恢复 → Failed → 全量重导会对已写批次重复写——非幂等 OnBatchWrite 消费方
    ///     须先按 BatchNo 回滚再重导；ErrorSummary（含 "stale recovery" 标记）指示部分进度（见 README/指南）；</item>
    /// <item><b>异常静默</b>：单条失败 LogWarning 继续；整体异常 LogWarning 返回 0——零破坏、绝不阻断宿主启动。</item>
    /// </list>
    /// <para>注册：<c>AddConstructibleService&lt;IStaleImportRecoveryService, StaleImportRecoveryService&gt;()</c>
    /// （对齐 IDataImportTaskService V0.1.4 形态）；消费方经 <c>User.Use&lt;IStaleImportRecoveryService&gt;()</c> 解析。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class StaleImportRecoveryService : DomainServiceBase, IStaleImportRecoveryService
    {
        private const int ScanBatchLimit = 500;

        private readonly DataPortOptions _options;
        private readonly ILogger<StaleImportRecoveryService> _logger;
        private DataImportRecordEntityDataService? _dataService;

        private DataImportRecordEntityDataService DataService => _dataService ??= User.Use<DataImportRecordEntityDataService>();

        public StaleImportRecoveryService(
            IDomainUser user,
            IOptions<DataPortOptions> options,
            ILogger<StaleImportRecoveryService> logger)
            : base(user)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<int> RecoverStaleImportsAsync(CancellationToken ct = default)
        {
            if (_options.ProcessingTimeoutMinutes <= 0)
                return 0;   // 0 = 禁用恢复扫描

            try
            {
                var staleBeforeUtc = DateTime.UtcNow.AddMinutes(-_options.ProcessingTimeoutMinutes);
                var staleRecords = await DataService.ListStaleProcessingAsync(staleBeforeUtc, ScanBatchLimit, ct);

                var recovered = 0;
                foreach (var record in staleRecords)
                {
                    try
                    {
                        var affected = await DataService.MarkStaleFailedAsync(
                            record.Id,
                            staleBeforeUtc,                       // CAS：UpdateTime < 阈值（scan→mark 间隙被心跳刷新则自然跳过）
                            $"stale recovery: Processing 超过 {_options.ProcessingTimeoutMinutes} 分钟",
                            DateTime.UtcNow,
                            ct);

                        // 影响行数 = 1 → 恢复；= 0 → 并发终态化/心跳刷新/他实例已恢复 → 跳过（CAS 消解竞态）
                        if (affected > 0) recovered++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "DataPort stale recovery failed for record {RecordId}（异常静默继续）", record.Id);
                    }
                }

                return recovered;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DataPort stale recovery scan failed（异常静默，返回 0）");
                return 0;
            }
        }
    }
}