using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志查询服务实现（internal sealed）——经 <see cref="AuditLogEntityDataService"/>（SG1 DataService）委托查询。
    /// <para>异常静默处理：查询失败时记录 Warning 日志并返回空结果（不抛出异常，不阻塞消费方）。</para>
    /// <para>数据访问红线整改（2026-09-07）：不直接注入 IFreeSql，动态 Where 用 Expression API 拼 predicate。</para>
    /// </summary>
    internal sealed class AuditLogQueryService : IAuditLogQueryService
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 200;

        private readonly AuditLogEntityDataService _dataService;
        private readonly ILogger<AuditLogQueryService> _logger;

        public AuditLogQueryService(AuditLogEntityDataService dataService, ILogger<AuditLogQueryService> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<AuditLogPagedResult> GetListAsync(AuditLogQueryInput query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            try
            {
                // V0.4.0（Oracle P2-2 单一真相源）：委托 DataService.SearchLogsAsync——谓词构建/分页/裁剪映射集中于此，
                // 管理 API 端点与服务层共享同一查询路径（不再本地 BuildPredicate）。
                return await _dataService.SearchLogsAsync(query, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志查询失败: GetListAsync");
                return new AuditLogPagedResult(0, Array.Empty<AuditLogListItemDto>());
            }
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(AuditLogQueryInput query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            try
            {
                // V0.4.0（Oracle P2-2 单一真相源）：谓词构建委托 DataService.BuildQueryPredicate（与 SearchLogsAsync 共享）。
                var predicate = AuditLogEntityDataService.BuildQueryPredicate(query);
                return await _dataService.CountAsync(predicate, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志统计失败: CountAsync");
                return 0;
            }
        }
    }
}