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
using TKWF.Ext.AuditLogging;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全事件审计分面 Reader（<see cref="AuditFacet.Security"/>）——经 <see cref="SecurityLogEntityDataService"/> 读取。
    /// <para>由 SecurityLog 扩展注册（仅启用 SecurityLog 时该面可用；AuditLogging 单独启用时查 Security 面硬失败 C1）。
    /// 集合注册 ADR92。AfterId keyset 供导出（C5）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class SecurityFacetReader : DomainServiceBase, IAuditFacetReader
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 200;

        private readonly ILogger<SecurityFacetReader> _logger;

        private SecurityLogEntityDataService? _dataService;
        private SecurityLogEntityDataService DataService => _dataService ??= User.Use<SecurityLogEntityDataService>();

        public SecurityFacetReader(IDomainUser user, ILogger<SecurityFacetReader> logger) : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public AuditFacet Facet => AuditFacet.Security;

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

        private static Expression<Func<SecurityLogEntity, bool>> BuildPredicate(AuditFacetQuery q)
        {
            var userName = q.ActorId;
            var eventType = q.EventType ?? q.ActionType;
            var result = q.Result;
            var correlationId = q.CorrelationId;
            var tenantId = ParseTenant(q.TenantId);
            var afterId = q.AfterId;
            var from = q.FromUtc?.UtcDateTime;
            var to = q.ToUtc?.UtcDateTime;

            return x =>
                (tenantId == null || x.TenantId == tenantId) &&
                (userName == null || x.UserName == userName) &&
                (eventType == null || x.EventType == eventType) &&
                (result == null || x.Result == result) &&
                (correlationId == null || x.CorrelationId == correlationId) &&
                (afterId == null || x.Id > afterId) &&
                (from == null || x.CreateTime >= from) &&
                (to == null || x.CreateTime <= to);
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

        private static SecurityAuditView Map(SecurityLogEntity e)
        {
            var actor = e.UserId is not null
                ? AuditActor.User(e.UserId.Value.ToString(), e.UserName)
                : !string.IsNullOrEmpty(e.UserName)
                    ? new AuditActor(AuditActorType.Anonymous, null, e.UserName)
                    : null;

            return new SecurityAuditView(
                e.Id,
                actor,
                null,
                e.EventType,
                null,
                e.CorrelationId,
                new DateTimeOffset(DateTime.SpecifyKind(e.CreateTime, DateTimeKind.Utc)),
                e.EventType,
                e.Result,
                e.IpAddress,
                e.UserName);
        }
    }
}
