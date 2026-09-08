using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志查询服务实现（internal sealed）——经 <see cref="SecurityLogEntityDataService"/>（SG1 DataService）委托查询。
    /// <para>异常静默处理：查询失败时记录 Warning 日志并返回空结果（不抛出异常，不阻塞消费方）。</para>
    /// <para>数据访问红线合规（2026-09-07）：不直接注入 IFreeSql / IEntityDAC，动态 Where 用 Expression API 拼 predicate。</para>
    /// </summary>
    internal sealed class SecurityLogQueryService : ISecurityLogQueryService
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 200;

        private readonly SecurityLogEntityDataService _dataService;
        private readonly ILogger<SecurityLogQueryService> _logger;

        public SecurityLogQueryService(SecurityLogEntityDataService dataService, ILogger<SecurityLogQueryService> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<SecurityLogPagedResult> GetListAsync(SecurityLogQueryInput input, CancellationToken ct = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            try
            {
                var (skip, take) = NormalizePaging(input.Skip, input.Take);
                var predicate = BuildPredicate(input);

                // DataService 转发方法——Count + List 并行避免重复构建 IQueryable
                var countTask = _dataService.CountAsync(predicate, ct);
                var listTask = _dataService.EntitySelectAsync(
                    predicate, skip, take,
                    q => q.OrderByDescending(e => e.CreateTime).ThenByDescending(e => e.Id),  // 时间倒序 + Id 决胜（同时间戳确定性）
                    ct);

                await Task.WhenAll(countTask, listTask);

                var total = await countTask;
                var entities = await listTask;

                var dtos = entities.Select(MapToListDto).ToList();
                return new SecurityLogPagedResult(total, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "安全日志查询失败: GetListAsync");
                return new SecurityLogPagedResult(0, Array.Empty<SecurityLogListItemDto>());
            }
        }

        /// <inheritdoc />
        public async Task<SecurityLogDetailDto?> GetDetailAsync(long id, CancellationToken ct = default)
        {
            try
            {
                var entity = await _dataService.EntityGetAsync(e => e.Id == id, ct);
                return entity == null ? null : MapToDetailDto(entity);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "安全日志详情查询失败: GetDetailAsync Id={Id}", id);
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(SecurityLogQueryInput input, CancellationToken ct = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            try
            {
                var predicate = BuildPredicate(input);
                return await _dataService.CountAsync(predicate, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "安全日志统计失败: CountAsync");
                return 0;
            }
        }

        /// <summary>用 Expression API 动态构建过滤 predicate（6 条件 AND 组合：用户名/IP/事件类型/结果/时间范围）。</summary>
        private static Expression<Func<SecurityLogEntity, bool>>? BuildPredicate(SecurityLogQueryInput input)
        {
            var param = Expression.Parameter(typeof(SecurityLogEntity), "e");
            Expression? combined = null;

            if (!string.IsNullOrEmpty(input.UserName))
                combined = Combine(combined, Contains(
                    param, nameof(SecurityLogEntity.UserName), input.UserName));

            if (!string.IsNullOrEmpty(input.IpAddress))
                combined = Combine(combined, Contains(
                    param, nameof(SecurityLogEntity.IpAddress), input.IpAddress));

            if (!string.IsNullOrEmpty(input.EventType))
                combined = Combine(combined, Expression.Equal(
                    Expression.Property(param, nameof(SecurityLogEntity.EventType)),
                    Expression.Constant(input.EventType)));

            if (!string.IsNullOrEmpty(input.Result))
                combined = Combine(combined, Expression.Equal(
                    Expression.Property(param, nameof(SecurityLogEntity.Result)),
                    Expression.Constant(input.Result)));

            if (input.FromUtc.HasValue)
                combined = Combine(combined, Expression.GreaterThanOrEqual(
                    Expression.Property(param, nameof(SecurityLogEntity.CreateTime)),
                    Expression.Constant(input.FromUtc.Value)));

            if (input.ToUtc.HasValue)
                combined = Combine(combined, Expression.LessThanOrEqual(
                    Expression.Property(param, nameof(SecurityLogEntity.CreateTime)),
                    Expression.Constant(input.ToUtc.Value)));

            return combined == null ? null : Expression.Lambda<Func<SecurityLogEntity, bool>>(combined, param);
        }

        /// <summary>属性包含匹配（string.Contains，nullable 列先 Coalesce 空串）。</summary>
        private static Expression Contains(ParameterExpression param, string propertyName, string value)
        {
            var prop = Expression.Property(param, propertyName);
            var safe = Expression.Coalesce(prop, Expression.Constant(string.Empty));
            return Expression.Call(
                safe,
                nameof(string.Contains),
                Type.EmptyTypes,
                Expression.Constant(value));
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

        /// <summary>投影列表 DTO（不含 Detail）。</summary>
        private static SecurityLogListItemDto MapToListDto(SecurityLogEntity entity)
        {
            return new SecurityLogListItemDto
            {
                Id = entity.Id,
                EventType = entity.EventType,
                EventCategory = entity.EventCategory,
                UserName = entity.UserName,
                UserId = entity.UserId,
                IpAddress = entity.IpAddress,
                Result = entity.Result,
                CreateTime = entity.CreateTime,
            };
        }

        /// <summary>投影详情 DTO（含 Detail 全文）。</summary>
        private static SecurityLogDetailDto MapToDetailDto(SecurityLogEntity entity)
        {
            return new SecurityLogDetailDto
            {
                Id = entity.Id,
                EventType = entity.EventType,
                EventCategory = entity.EventCategory,
                UserName = entity.UserName,
                UserId = entity.UserId,
                IpAddress = entity.IpAddress,
                UserAgent = entity.UserAgent,
                Result = entity.Result,
                Detail = entity.Detail,
                CorrelationId = entity.CorrelationId,
                CreateTime = entity.CreateTime,
            };
        }
    }
}
