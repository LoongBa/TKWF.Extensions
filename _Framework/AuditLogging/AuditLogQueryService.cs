using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using FreeSql;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志查询服务实现（internal sealed）——基于 FreeSql 按条件分页查询审计日志。
    /// <para>异常静默处理：查询失败时记录 Warning 日志并返回空结果（不抛出异常，不阻塞消费方）。</para>
    /// <para>对齐 <see cref="FreeSqlAuditLogStore"/> 模式：internal 隐藏实现细节、TryAddScoped 允许消费方覆盖。</para>
    /// </summary>
    internal sealed class AuditLogQueryService : IAuditLogQueryService
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 200;

        private readonly IFreeSql _freeSql;
        private readonly ILogger<AuditLogQueryService> _logger;

        /// <summary>
        /// 初始化审计日志查询服务。
        /// </summary>
        /// <param name="freeSql">FreeSql 实例。</param>
        /// <param name="logger">日志记录器。</param>
        public AuditLogQueryService(IFreeSql freeSql, ILogger<AuditLogQueryService> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<AuditLogPagedResult> GetListAsync(AuditLogQueryInput query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            try
            {
                var (skip, take) = NormalizePaging(query.Skip, query.Take);

                var itemsQuery = ApplyFilters(query);
                var countTask = itemsQuery.CountAsync(ct);
                var listTask = itemsQuery
                    .OrderByDescending(e => e.ExecutionTime)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(ct);

                await Task.WhenAll(countTask, listTask);

                var total = await countTask;
                var entities = await listTask;

                var dtos = entities.Select(MapToDto).ToList();
                return new AuditLogPagedResult(total, dtos);
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
                return await ApplyFilters(query).CountAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志统计失败: CountAsync");
                return 0;
            }
        }

        /// <summary>
        /// 构建 FreeSql 查询——按输入条件动态添加 Where 过滤。
        /// </summary>
        private ISelect<AuditLogEntity> ApplyFilters(AuditLogQueryInput query)
        {
            var select = _freeSql.Select<AuditLogEntity>();

            if (query.StartTime.HasValue)
                select = select.Where(e => e.ExecutionTime >= query.StartTime.Value);

            if (query.EndTime.HasValue)
                select = select.Where(e => e.ExecutionTime <= query.EndTime.Value);

            if (!string.IsNullOrEmpty(query.UserName))
                select = select.Where(e => e.UserName != null && e.UserName.Contains(query.UserName));

            if (!string.IsNullOrEmpty(query.UserId))
                select = select.Where(e => e.UserId == query.UserId);

            if (!string.IsNullOrEmpty(query.ServiceName))
                select = select.Where(e => e.ServiceName == query.ServiceName);

            if (!string.IsNullOrEmpty(query.MethodName))
                select = select.Where(e => e.MethodName == query.MethodName);

            if (query.Success.HasValue)
                select = select.Where(e => e.Success == query.Success.Value);

            if (!string.IsNullOrEmpty(query.CorrelationId))
                select = select.Where(e => e.CorrelationId == query.CorrelationId);

            if (query.MinDurationMs.HasValue)
                select = select.Where(e => e.DurationMs >= query.MinDurationMs.Value);

            if (query.MaxDurationMs.HasValue)
                select = select.Where(e => e.DurationMs <= query.MaxDurationMs.Value);

            return select;
        }

        /// <summary>
        /// 规范化分页参数——Take 默认 50，上限 200（防滥用）；Skip 下限 0。
        /// </summary>
        private static (int Skip, int Take) NormalizePaging(int skip, int take)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
            return (skip, take);
        }

        /// <summary>
        /// 将 <see cref="AuditLogEntity"/> 投影为 <see cref="AuditLogListItemDto"/>（不含 ArgumentsJson）。
        /// </summary>
        private static AuditLogListItemDto MapToDto(AuditLogEntity entity)
        {
            return new AuditLogListItemDto
            {
                Id = entity.Id,
                UserName = entity.UserName,
                UserId = entity.UserId,
                ServiceName = entity.ServiceName,
                MethodName = entity.MethodName,
                ExecutionTime = entity.ExecutionTime,
                DurationMs = entity.DurationMs,
                Success = entity.Success,
                Exception = entity.Exception,
                CorrelationId = entity.CorrelationId,
                CreateTime = entity.CreateTime
            };
        }
    }
}
