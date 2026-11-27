using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 分面读取契约（每面一实现，集合注册 ADR92）。写门槛面（调用/业务在 AuditLogging，安全在 SecurityLog）自行注册实现。
    /// </summary>
    public interface IAuditFacetReader : IDomainService
    {
        /// <summary>本 Reader 服务的审计面。</summary>
        AuditFacet Facet { get; }

        /// <summary>分面查询（支持 keyset <c>AfterId</c>）。</summary>
        Task<AuditFacetPageResult> QueryAsync(AuditFacetQuery query, CancellationToken ct = default);

        /// <summary>按面内 Id 取详情。</summary>
        Task<AuditRecordView?> GetDetailAsync(long id, CancellationToken ct = default);

        /// <summary>按条件计数。</summary>
        Task<long> CountAsync(AuditFacetQuery query, CancellationToken ct = default);

        /// <summary>水位增量（Id 升序，Id &gt; afterId；用于 G8/Outbox/导出）。</summary>
        Task<IReadOnlyList<AuditRecordView>> GetIncrementalAsync(long afterId, int batch, CancellationToken ct = default);
    }

    /// <summary>
    /// 审计统一读门面（域内门面，<b>不加 [GenerateController]</b>；对外暴露仅业务审计面，C6）。
    /// <para>定向查询缺面 → 抛 <see cref="AuditFacetUnavailableException"/>（C1，禁静默空）；跨面聚合只含已注册面并在结果中标注。</para>
    /// </summary>
    public interface IAuditQueryService : IDomainService
    {
        /// <summary>当前已注册可用的审计面。</summary>
        IReadOnlyList<AuditFacet> GetAvailableFacets();

        /// <summary>分面查询（facet 定向）。缺面抛 <see cref="AuditFacetUnavailableException"/>。</summary>
        Task<AuditFacetPageResult> QueryAsync(AuditFacet facet, AuditFacetQuery query, CancellationToken ct = default);

        /// <summary>分面详情。</summary>
        Task<AuditRecordView?> GetDetailAsync(AuditFacet facet, long id, CancellationToken ct = default);

        /// <summary>分面计数。</summary>
        Task<long> CountAsync(AuditFacet facet, AuditFacetQuery query, CancellationToken ct = default);

        /// <summary>按 CorrelationId 跨面聚合（只含已注册面，返回标注参与面）。</summary>
        Task<AuditCrossFacetResult> GetByCorrelationAsync(string correlationId, CancellationToken ct = default);

        /// <summary>分面水位增量。</summary>
        Task<IReadOnlyList<AuditRecordView>> GetIncrementalAsync(AuditFacet facet, long afterId, int batch, CancellationToken ct = default);
    }
}
