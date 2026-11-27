using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 业务事实审计分面 Reader（<see cref="AuditFacet.Business"/>）——经 <see cref="BusinessAuditEntityDataService"/> 读取。
    /// <para>AfterId keyset 供导出/水位（C5）；TenantId 预留（C11）。集合注册 ADR92。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class BusinessFacetReader : DomainServiceBase, IAuditFacetReader
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 200;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ILogger<BusinessFacetReader> _logger;

        private BusinessAuditEntityDataService? _dataService;
        private BusinessAuditEntityDataService DataService => _dataService ??= User.Use<BusinessAuditEntityDataService>();

        public BusinessFacetReader(IDomainUser user, ILogger<BusinessFacetReader> logger) : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public AuditFacet Facet => AuditFacet.Business;

        /// <inheritdoc />
        public async Task<AuditFacetPageResult> QueryAsync(AuditFacetQuery query, CancellationToken ct = default)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            var (skip, take) = NormalizePaging(query.Skip, query.Take);
            var predicate = BuildPredicate(query);

            var total = await DataService.CountAsync(predicate, ct);
            var rows = await DataService.EntitySelectAsync(
                predicate, skip, take, q => q.OrderBy(x => x.Id), ct);

            return new AuditFacetPageResult(total, rows.Select(Map).ToList());
        }

        /// <inheritdoc />
        public async Task<AuditRecordView?> GetDetailAsync(long id, CancellationToken ct = default)
        {
            var entity = await DataService.EntityGetAsync(x => x.Id == id, ct);
            return entity is null ? null : Map(entity);
        }

        /// <inheritdoc />
        public Task<long> CountAsync(AuditFacetQuery query, CancellationToken ct = default)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            return DataService.CountAsync(BuildPredicate(query), ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<AuditRecordView>> GetIncrementalAsync(long afterId, int batch, CancellationToken ct = default)
        {
            var take = Math.Clamp(batch <= 0 ? DefaultTake : batch, 1, MaxTake);
            var rows = await DataService.EntitySelectAsync(
                x => x.Id > afterId, 0, take, q => q.OrderBy(x => x.Id), ct);
            return rows.Select(Map).ToList();
        }

        private static Expression<Func<BusinessAuditEntity, bool>> BuildPredicate(AuditFacetQuery q)
        {
            var tenantId = ParseTenant(q.TenantId);
            var targetType = q.TargetType;
            var targetId = q.TargetId;
            var actionType = q.ActionType;
            var actorType = q.ActorType;
            var actorId = q.ActorId;
            var correlationId = q.CorrelationId;
            var afterId = q.AfterId;
            var from = q.FromUtc?.UtcDateTime;
            var to = q.ToUtc?.UtcDateTime;

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

        private static long? ParseTenant(string? tenant)
        {
            if (string.IsNullOrWhiteSpace(tenant)) return null;
            if (!long.TryParse(tenant, out var parsed))
                throw new ArgumentException($"TenantId 必须为数字，收到 '{tenant}'（fail-closed，防跨租户越权）");
            return parsed;
        }

        private static (int Skip, int Take) NormalizePaging(int skip, int take)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
            return (skip, take);
        }

        private static BusinessAuditView Map(BusinessAuditEntity e)
        {
            var actor = Enum.TryParse<AuditActorType>(e.ActorType, ignoreCase: true, out var type)
                ? new AuditActor(type, e.ActorId, e.ActorName)
                : new AuditActor(AuditActorType.System, e.ActorId, e.ActorName);

            return new BusinessAuditView(
                e.Id,
                actor,
                new AuditTarget(e.TargetType, e.TargetId),
                e.ActionType,
                e.Summary,
                e.CorrelationId,
                new DateTimeOffset(DateTime.SpecifyKind(e.ExecutionTime, DateTimeKind.Utc)),
                null,
                null,
                ParseAttributes(e.AttributesJson));
        }

        private static IReadOnlyDictionary<string, object?>? ParseAttributes(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
