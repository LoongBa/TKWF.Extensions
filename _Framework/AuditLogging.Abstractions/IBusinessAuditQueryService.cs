using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 业务事实审计查询门面（v0.6 单面，诚实命名）。
    /// <para>统一跨面 <c>IAuditQueryService</c> 为 v0.7 additive（届时新增接口，不破坏本契约）。</para>
    /// </summary>
    public interface IBusinessAuditQueryService : IDomainService
    {
        Task<BusinessAuditPagedResult> GetListAsync(BusinessAuditQueryInput input, CancellationToken ct = default);

        Task<BusinessAuditListItemDto?> GetDetailAsync(long id, CancellationToken ct = default);

        /// <summary>受控详情（含 Before/After/AttributesJson；需 <see cref="AuditOperation.Detail"/> 放行）。</summary>
        Task<BusinessAuditDetailDto?> GetDetailWithPayloadAsync(long id, CancellationToken ct = default);

        Task<long> CountAsync(BusinessAuditQueryInput input, CancellationToken ct = default);
    }
}
