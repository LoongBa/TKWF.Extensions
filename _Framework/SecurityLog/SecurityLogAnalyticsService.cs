using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志分析服务实现（internal sealed，v0.2.0）——经 <see cref="SecurityLogEntityDataService"/>（SG1 DataService）委托聚合/清理。
    /// <para>异常静默处理：聚合/清理失败时记录 Warning 日志并返回空结果/0（对齐 <see cref="SecurityLogQueryService"/> 既有模式，
    /// 不抛出异常，不阻塞消费方）。</para>
    /// <para>数据访问红线合规（2026-09-07）：不注入 IFreeSql / IEntityDAC——只依赖 DataService + <see cref="IOptions{TOptions}"/>
    /// + <see cref="ILogger{TCategoryName}"/>。</para>
    /// </summary>
    internal sealed class SecurityLogAnalyticsService : ISecurityLogAnalyticsService
    {
        /// <summary>TopN 默认值（接口签名默认参数）。</summary>
        private const int DefaultTopN = 10;

        /// <summary>TopN 上限——防滥用（对齐 QueryService MaxTake=200 的防护精神）。</summary>
        private const int MaxTopN = 100;

        private readonly SecurityLogEntityDataService _dataService;
        private readonly IOptions<SecurityLoggingOptions> _options;
        private readonly ILogger<SecurityLogAnalyticsService> _logger;

        public SecurityLogAnalyticsService(
            SecurityLogEntityDataService dataService,
            IOptions<SecurityLoggingOptions> options,
            ILogger<SecurityLogAnalyticsService> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SecurityLogFailureStat>> GetTopFailedUsersAsync(
            int topN = DefaultTopN, TimeSpan? window = null, CancellationToken ct = default)
        {
            try
            {
                var (from, to) = ResolveWindow(window);
                var stats = await _dataService.GetTopFailedByUserAsync(from, to, NormalizeTopN(topN), ct);
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "安全日志失败次数聚合失败: GetTopFailedUsersAsync");
                return Array.Empty<SecurityLogFailureStat>();
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SecurityLogFailureStat>> GetTopFailedIpsAsync(
            int topN = DefaultTopN, TimeSpan? window = null, CancellationToken ct = default)
        {
            try
            {
                var (from, to) = ResolveWindow(window);
                var stats = await _dataService.GetTopFailedByIpAsync(from, to, NormalizeTopN(topN), ct);
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "安全日志失败次数聚合失败: GetTopFailedIpsAsync");
                return Array.Empty<SecurityLogFailureStat>();
            }
        }

        /// <inheritdoc />
        public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
        {
            try
            {
                var options = _options.Value;
                var retentionDays = Math.Max(0, options.RetentionDays);
                var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);
                var batchSize = Math.Max(1, options.CleanupBatchSize);

                int totalDeleted = 0;
                while (true)
                {
                    // 每批独立查询 + 删除（DeleteExpiredAsync 内部各自 QueryForUser，防 FreeSql ISelect 原地可变陷阱）
                    var deleted = await _dataService.DeleteExpiredAsync(cutoffUtc, batchSize, ct);
                    if (deleted <= 0) break;             // 无过期记录 → 清完
                    totalDeleted += deleted;
                    if (deleted < batchSize) break;      // 不足一批 → 本批已清空剩余
                }
                return totalDeleted;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "安全日志保留清理失败: CleanupExpiredAsync");
                return 0;
            }
        }

        /// <summary>规范化 TopN：&lt;=0 回退默认，上限 100。</summary>
        private static int NormalizeTopN(int topN)
            => Math.Clamp(topN <= 0 ? DefaultTopN : topN, 1, MaxTopN);

        /// <summary>解析时间窗口：null = 全量（起止均为 null，不限制时间）；否则 [UtcNow - window, UtcNow] 闭区间。</summary>
        private static (DateTime? FromUtc, DateTime? ToUtc) ResolveWindow(TimeSpan? window)
        {
            if (!window.HasValue || window.Value <= TimeSpan.Zero)
                return (null, null);

            var now = DateTime.UtcNow;
            return (now.Subtract(window.Value), now);
        }
    }
}