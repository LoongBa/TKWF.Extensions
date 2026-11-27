using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 业务事实审计查询门面（v0.6 单面）——经 SG1 <see cref="BusinessAuditEntityDataService"/> 委托查询。
    /// 列表 DTO 不含 Before/After 全文（防 PII）。查询失败异常静默返回空结果。
    /// </summary>
    [DiContractIgnore]
    internal sealed class BusinessAuditQueryService : DomainServiceBase, IBusinessAuditQueryService
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 200;

        private readonly ILogger<BusinessAuditQueryService> _logger;

        private BusinessAuditEntityDataService? _dataService;
        private BusinessAuditEntityDataService DataService => _dataService ??= User.Use<BusinessAuditEntityDataService>();

        private IAuditAccessPolicy? _accessPolicy;
        private IAuditAccessPolicy? AccessPolicy => _accessPolicy ??= User.Use<IAuditAccessPolicy>();

        public BusinessAuditQueryService(IDomainUser user, ILogger<BusinessAuditQueryService> logger)
            : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<BusinessAuditPagedResult> GetListAsync(BusinessAuditQueryInput input, CancellationToken ct = default)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));

            try
            {
                var (allowed, tenantFilter, reason) = await AuthorizeAsync(input, ct);
                if (!allowed)
                {
                    _logger.LogWarning("业务审计查询被拒绝：{Reason}", reason);
                    throw new AuditAccessDeniedException(reason);
                }

                var predicate = BuildPredicate(input, tenantFilter);
                var take = input.Take <= 0 ? DefaultTake : Math.Min(input.Take, MaxTake);
                var skip = Math.Max(0, input.Skip);

                var total = await DataService.CountAsync(predicate, ct);
                var rows = await DataService.EntitySelectAsync(
                    predicate, skip, take, q => q.OrderByDescending(x => x.Id), ct);

                return new BusinessAuditPagedResult(total, rows.Select(Map).ToList());
            }
            catch (AuditAccessDeniedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "业务审计查询失败: GetListAsync");
                return new BusinessAuditPagedResult(0, Array.Empty<BusinessAuditListItemDto>());
            }
        }

        public async Task<BusinessAuditListItemDto?> GetDetailAsync(long id, CancellationToken ct = default)
        {
            try
            {
                var access = await EvaluateAsync(AuditOperation.Detail, null, ct);
                if (!access.Allowed)
                {
                    _logger.LogWarning("业务审计详情被拒绝：{Reason}", access.Reason);
                    throw new AuditAccessDeniedException(access.Reason);
                }

                var entity = await DataService.EntityGetAsync(x => x.Id == id, ct);
                return entity is null ? null : Map(entity);
            }
            catch (AuditAccessDeniedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "业务审计详情查询失败: GetDetailAsync id={Id}", id);
                return null;
            }
        }

        public async Task<long> CountAsync(BusinessAuditQueryInput input, CancellationToken ct = default)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));

            try
            {
                var (allowed, tenantFilter, reason) = await AuthorizeAsync(input, ct);
                if (!allowed)
                {
                    _logger.LogWarning("业务审计统计被拒绝：{Reason}", reason);
                    throw new AuditAccessDeniedException(reason);
                }

                return await DataService.CountAsync(BuildPredicate(input, tenantFilter), ct);
            }
            catch (AuditAccessDeniedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "业务审计统计失败: CountAsync");
                return 0;
            }
        }

        private async Task<(bool Allowed, string? TenantFilter, string? Reason)> AuthorizeAsync(
            BusinessAuditQueryInput input, CancellationToken ct)
        {
            var operation = string.IsNullOrWhiteSpace(input.TenantId) ? AuditOperation.QueryAll : AuditOperation.QueryOwn;
            var decision = await EvaluateAsync(operation, input.TenantId, ct);
            if (!decision.Allowed) return (false, null, decision.Reason);

            var tenantFilter = operation == AuditOperation.QueryOwn ? User.TenantId?.ToString() : input.TenantId;
            if (operation == AuditOperation.QueryOwn && tenantFilter is null)
            {
                return (false, null, "QueryOwn 调用方无租户（fail-closed，防跨租户读）");
            }
            return (true, tenantFilter, null);
        }

        private async Task<AuditAccessDecision> EvaluateAsync(AuditOperation operation, string? tenantId, CancellationToken ct)
        {
            var policy = AccessPolicy;
            if (policy is null) return AuditAccessDecision.Deny("审计访问策略未注册（fail-closed）");
            return await policy.EvaluateAsync(
                new AuditAccessRequest(operation, User.UserId, tenantId, User.IsAuthenticated), ct);
        }

        private static Expression<Func<BusinessAuditEntity, bool>> BuildPredicate(
            BusinessAuditQueryInput input, string? tenantFilter)
        {
            long? tenantId = null;
            if (!string.IsNullOrWhiteSpace(tenantFilter))
            {
                if (!long.TryParse(tenantFilter, out var parsedTenant))
                {
                    throw new ArgumentException($"TenantId 必须为数字，收到 '{tenantFilter}'（fail-closed，防跨租户越权）");
                }
                tenantId = parsedTenant;
            }
            var targetType = input.TargetType;
            var targetId = input.TargetId;
            var actionType = input.ActionType;
            var actorType = input.ActorType;
            var actorId = input.ActorId;
            var correlationId = input.CorrelationId;
            var afterId = input.AfterId;
            var from = input.FromUtc?.UtcDateTime;
            var to = input.ToUtc?.UtcDateTime;

            return x =>
                (tenantId == null || x.TenantId == tenantId) &&
                (targetType == null || x.TargetType == targetType) &&
                (targetId == null || x.TargetId == targetId) &&
                (actionType == null || x.ActionType == actionType) &&
                (actorType == null || x.ActorType == actorType) &&
                (actorId == null || x.ActorId == actorId) &&
                (correlationId == null || x.CorrelationId == correlationId) &&
                (afterId == null || x.Id > afterId) &&
                (from == null || x.ExecutionTime >= from) &&
                (to == null || x.ExecutionTime <= to);
        }

        private static BusinessAuditListItemDto Map(BusinessAuditEntity e) => new(
            e.Id,
            e.TenantId?.ToString(),
            e.ActorType,
            e.ActorId,
            e.ActorName,
            e.TargetType,
            e.TargetId,
            e.ActionType,
            e.Summary,
            e.CorrelationId,
            new DateTimeOffset(DateTime.SpecifyKind(e.ExecutionTime, DateTimeKind.Utc)),
            e.CreateTime);
    }
}
