using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部 IdP 票据 SPA 兑现契约基接口（V0.3.0 契约包提取——业务签名进契约包，端点面留主包门面）。
/// <para>鉴权语义：先认证（验票）后断言（身份/签发）；P1-3 统一判因 TICKET_INVALID；P2-4 频控强制（ext:ticket:{ip}，fail-closed）。</para>
/// </summary>
public interface IExternalTicketLoginServiceContract : IDomainService
{
    /// <summary>票据 SPA 兑现登录——验票 → 取 FederatedIdentity → 委托 IFederatedLoginOrchestrator → 签 token1 → TokenResponse。</summary>
    [AllowAnonymousFlag]
    Task<TokenResponse> LoginAsync(ExternalTicketLoginRequest request, CancellationToken ct = default);
}
