using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 业务审计 Web 门面（REST 契约）——委托 <see cref="IBusinessAuditQueryService"/>（访问策略/租户门控在域门面内）。
    /// <para>列表返回安全 DTO（不含 Before/After/AttributesJson）；详情同安全 DTO。请求体为
    /// <see cref="BusinessAuditQueryInput"/>（POST 绑定，避免 GET 复杂类型绑定问题）。</para>
    /// </summary>
    [ApiExpose(Alias = "business-audit")]
    public interface IBusinessAuditWebApiService : IDomainService
    {
        [ApiExpose(RestRoute = "/business-audit/query", RestMethod = "POST")]
        Task<BusinessAuditPagedResult> QueryAsync(BusinessAuditQueryInput input, CancellationToken ct = default);

        [ApiExpose(RestRoute = "/business-audit/{id}", RestMethod = "GET")]
        Task<BusinessAuditListItemDto?> GetDetailAsync(long id, CancellationToken ct = default);
    }
}
