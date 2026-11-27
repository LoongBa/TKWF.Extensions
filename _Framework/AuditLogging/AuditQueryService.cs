using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计统一读门面（<see cref="IAuditQueryService"/>）——按面下发到已注册 <see cref="IAuditFacetReader"/> 集合。
    /// <para><b>C1</b>：定向查询缺面 → 抛 <see cref="AuditFacetUnavailableException"/>（禁静默空）；
    /// 跨面聚合只枚举已注册面，返回结果显式标注参与面。<b>C6</b>：本门面无 <c>[GenerateController]</c>，不自动 REST。</para>
    /// <para>集合经 <c>User.GetService&lt;IEnumerable&lt;IAuditFacetReader&gt;&gt;()</c> 懒解析（帧内）；ADR92 工厂需 CurrentAopUser。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class AuditQueryService : DomainServiceBase, IAuditQueryService
    {
        private readonly ILogger<AuditQueryService> _logger;

        private IReadOnlyList<IAuditFacetReader>? _readers;

        public AuditQueryService(IDomainUser user, ILogger<AuditQueryService> logger) : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private IReadOnlyList<IAuditFacetReader> Readers =>
            _readers ??= (User.GetService<IEnumerable<IAuditFacetReader>>() ?? Enumerable.Empty<IAuditFacetReader>()).ToList();

        /// <inheritdoc />
        public IReadOnlyList<AuditFacet> GetAvailableFacets()
            => Readers.Select(r => r.Facet).Distinct().ToList();

        /// <inheritdoc />
        public Task<AuditFacetPageResult> QueryAsync(AuditFacet facet, AuditFacetQuery query, CancellationToken ct = default)
            => Require(facet).QueryAsync(query, ct);

        /// <inheritdoc />
        public Task<AuditRecordView?> GetDetailAsync(AuditFacet facet, long id, CancellationToken ct = default)
            => Require(facet).GetDetailAsync(id, ct);

        /// <inheritdoc />
        public Task<long> CountAsync(AuditFacet facet, AuditFacetQuery query, CancellationToken ct = default)
            => Require(facet).CountAsync(query, ct);

        /// <inheritdoc />
        public async Task<AuditCrossFacetResult> GetByCorrelationAsync(string correlationId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(correlationId))
                throw new ArgumentException("CorrelationId 不能为空。", nameof(correlationId));

            var readers = Readers;
            var items = new List<AuditRecordView>();
            foreach (var reader in readers)
            {
                var page = await reader.QueryAsync(
                    new AuditFacetQuery { CorrelationId = correlationId, Take = 200 }, ct);
                items.AddRange(page.Items);
            }

            return new AuditCrossFacetResult(readers.Select(r => r.Facet).Distinct().ToList(), items);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AuditRecordView>> GetIncrementalAsync(
            AuditFacet facet, long afterId, int batch, CancellationToken ct = default)
            => Require(facet).GetIncrementalAsync(afterId, batch, ct);

        private IAuditFacetReader Require(AuditFacet facet)
        {
            var reader = Readers.FirstOrDefault(r => r.Facet == facet);
            if (reader is null)
                throw new AuditFacetUnavailableException(facet);
            return reader;
        }
    }
}
