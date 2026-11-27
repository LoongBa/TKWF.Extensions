using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuditLogging;

namespace TKWF.Ext.AuditLogging.Web
{
    /// <summary>
    /// 业务审计 Web 门面实现（<c>[GenerateController]</c> → SG1b B4 端点注册表）——委托域门面
    /// <see cref="IBusinessAuditQueryService"/>（拒绝→<c>AuditAccessDeniedException</c>；租户/策略门控已内置）。
    /// </summary>
    [GenerateController]
    public sealed class BusinessAuditWebApiService : DomainServiceBase, IBusinessAuditWebApiService
    {
        public BusinessAuditWebApiService(IDomainUser user) : base(user) { }

        public async Task<BusinessAuditPagedResult> QueryAsync(BusinessAuditQueryInput input, CancellationToken ct = default)
            => await User.Use<IBusinessAuditQueryService>().GetListAsync(input, ct);

        public async Task<BusinessAuditListItemDto?> GetDetailAsync(long id, CancellationToken ct = default)
            => await User.Use<IBusinessAuditQueryService>().GetDetailAsync(id, ct);

        public async Task<BusinessAuditDetailDto?> GetDetailWithPayloadAsync(long id, CancellationToken ct = default)
            => await User.Use<IBusinessAuditQueryService>().GetDetailWithPayloadAsync(id, ct);
    }
}
