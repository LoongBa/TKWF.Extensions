using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuditLogging;

namespace TKWF.Ext.AuditLogging.Web
{
    /// <summary>
    /// 业务审计 Web 门面（REST 契约，实现于本程序集——CTRL009 同程序集约束）——委托
    /// <see cref="IBusinessAuditQueryService"/>（访问策略/租户门控在域门面内）。
    /// <para>列表返回安全 DTO（不含 Before/After/AttributesJson）；详情同安全 DTO；
    /// <c>{id}/detail</c> 为受控详情（含载荷，需 <see cref="AuditOperation.Detail"/> 放行）。</para>
    /// </summary>
    [ApiExpose(Alias = "business-audit")]
    public interface IBusinessAuditWebApiService : IDomainService
    {
        [ApiExpose(RestRoute = "/business-audit/query", RestMethod = "POST")]
        Task<BusinessAuditPagedResult> QueryAsync(BusinessAuditQueryInput input, CancellationToken ct = default);

        [ApiExpose(RestRoute = "/business-audit/{id}", RestMethod = "GET")]
        Task<BusinessAuditListItemDto?> GetDetailAsync(long id, CancellationToken ct = default);

        [ApiExpose(RestRoute = "/business-audit/{id}/detail", RestMethod = "GET")]
        Task<BusinessAuditDetailDto?> GetDetailWithPayloadAsync(long id, CancellationToken ct = default);
    }
}
