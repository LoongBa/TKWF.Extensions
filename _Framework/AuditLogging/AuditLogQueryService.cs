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
                var (skip, take) = NormalizePaging(query.Skip, query.Take);
                var predicate = BuildPredicate(query);

                // DataService 基类转发方法（.g.cs 内部访问器）——Count + List 并行避免重复构建 IQueryable
                var countTask = _dataService.CountAsync(predicate, ct);
                var listTask = _dataService.EntitySelectAsync(
                    predicate, skip, take, q => q.OrderByDescending(e => e.ExecutionTime), ct);

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
                var predicate = BuildPredicate(query);
                return await _dataService.CountAsync(predicate, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志统计失败: CountAsync");
                return 0;
            }
        }

        /// <summary>用 Expression API 动态构建过滤 predicate（10 条件 AND 组合）。</summary>
        private static Expression<Func<AuditLogEntity, bool>>? BuildPredicate(AuditLogQueryInput query)
        {
            var param = Expression.Parameter(typeof(AuditLogEntity), "e");
            Expression? combined = null;

            if (query.StartTime.HasValue)
                combined = Combine(combined, Expression.GreaterThanOrEqual(
                    Expression.Property(param, nameof(AuditLogEntity.ExecutionTime)),
                    Expression.Constant(query.StartTime.Value)));

            if (query.EndTime.HasValue)
                combined = Combine(combined, Expression.LessThanOrEqual(
                    Expression.Property(param, nameof(AuditLogEntity.ExecutionTime)),
                    Expression.Constant(query.EndTime.Value)));

            if (!string.IsNullOrEmpty(query.UserName))
            {
                var userNameProp = Expression.Property(param, nameof(AuditLogEntity.UserName));
                var contains = Expression.Call(
                    Expression.Coalesce(userNameProp, Expression.Constant(string.Empty)),
                    nameof(string.Contains),
                    Type.EmptyTypes,
                    Expression.Constant(query.UserName));
                combined = Combine(combined, contains);
            }

            if (!string.IsNullOrEmpty(query.UserId))
                combined = Combine(combined, Expression.Equal(
                    Expression.Property(param, nameof(AuditLogEntity.UserId)),
                    Expression.Constant(query.UserId)));

            if (!string.IsNullOrEmpty(query.ServiceName))
                combined = Combine(combined, Expression.Equal(
                    Expression.Property(param, nameof(AuditLogEntity.ServiceName)),
                    Expression.Constant(query.ServiceName)));

            if (!string.IsNullOrEmpty(query.MethodName))
                combined = Combine(combined, Expression.Equal(
                    Expression.Property(param, nameof(AuditLogEntity.MethodName)),
                    Expression.Constant(query.MethodName)));

            if (query.Success.HasValue)
                combined = Combine(combined, Expression.Equal(
                    Expression.Property(param, nameof(AuditLogEntity.Success)),
                    Expression.Constant(query.Success.Value)));

            if (!string.IsNullOrEmpty(query.CorrelationId))
                combined = Combine(combined, Expression.Equal(
                    Expression.Property(param, nameof(AuditLogEntity.CorrelationId)),
                    Expression.Constant(query.CorrelationId)));

            if (query.MinDurationMs.HasValue)
                combined = Combine(combined, Expression.GreaterThanOrEqual(
                    Expression.Property(param, nameof(AuditLogEntity.DurationMs)),
                    Expression.Constant(query.MinDurationMs.Value)));

            if (query.MaxDurationMs.HasValue)
                combined = Combine(combined, Expression.LessThanOrEqual(
                    Expression.Property(param, nameof(AuditLogEntity.DurationMs)),
                    Expression.Constant(query.MaxDurationMs.Value)));

            return combined == null ? null : Expression.Lambda<Func<AuditLogEntity, bool>>(combined, param);
        }

        private static Expression Combine(Expression? left, Expression right)
            => left == null ? right : Expression.AndAlso(left, right);

        /// <summary>规范化分页参数——Take 默认 50，上限 200（防滥用）；Skip 下限 0。</summary>
        private static (int Skip, int Take) NormalizePaging(int skip, int take)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
            return (skip, take);
        }

        /// <summary>将 <see cref="AuditLogEntity"/> 投影为 <see cref="AuditLogListItemDto"/>（不含 ArgumentsJson）。</summary>
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