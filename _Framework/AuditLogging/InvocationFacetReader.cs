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
    /// 调用审计分面 Reader（<see cref="AuditFacet.Invocation"/>）——经 <see cref="AuditLogEntityDataService"/> 读取。
    /// <para>集合注册 ADR92（TryAddEnumerableConstructible），门面帧内枚举。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class InvocationFacetReader : DomainServiceBase, IAuditFacetReader
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 200;

        private readonly ILogger<InvocationFacetReader> _logger;

        private AuditLogEntityDataService? _dataService;
        private AuditLogEntityDataService DataService => _dataService ??= User.Use<AuditLogEntityDataService>();

        public InvocationFacetReader(IDomainUser user, ILogger<InvocationFacetReader> logger) : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public AuditFacet Facet => AuditFacet.Invocation;

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

        private static Expression<Func<AuditLogEntity, bool>> BuildPredicate(AuditFacetQuery q)
        {
            var actorId = q.ActorId;
            var serviceName = q.TargetType;
            var methodName = q.TargetId;
            var correlationId = q.CorrelationId;
            var afterId = q.AfterId;
            var from = q.FromUtc?.UtcDateTime;
            var to = q.ToUtc?.UtcDateTime;

            return x =>
                (actorId == null || x.UserId == actorId) &&
                (serviceName == null || x.ServiceName == serviceName) &&
                (methodName == null || x.MethodName == methodName) &&
                (correlationId == null || x.CorrelationId == correlationId) &&
                (afterId == null || x.Id > afterId) &&
                (from == null || x.ExecutionTime >= from) &&
                (to == null || x.ExecutionTime <= to);
        }

        private static (int Skip, int Take) NormalizePaging(int skip, int take)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
            return (skip, take);
        }

        private static InvocationAuditView Map(AuditLogEntity e)
        {
            var actor = e.UserId is not null
                ? AuditActor.User(e.UserId, e.UserName)
                : e.UserName is not null
                    ? new AuditActor(AuditActorType.Anonymous, null, e.UserName)
                    : null;

            return new InvocationAuditView(
                e.Id,
                actor,
                new AuditTarget(e.ServiceName, e.MethodName),
                e.MethodName,
                e.ArgumentsJson,
                e.CorrelationId,
                new DateTimeOffset(DateTime.SpecifyKind(e.ExecutionTime, DateTimeKind.Utc)),
                e.ServiceName,
                e.MethodName,
                e.DurationMs,
                e.Success);
        }
    }
}
