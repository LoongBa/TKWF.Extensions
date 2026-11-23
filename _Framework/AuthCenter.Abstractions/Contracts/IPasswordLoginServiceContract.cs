using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 密码登录契约基接口（V0.3.0 契约包提取——业务签名进契约包，端点面留主包门面）。
/// <para>鉴权语义：先认证后断言；本契约不提供裸凭据校验原语（B2 安全约定）。</para>
/// <remarks>⚠️ 纯契约包无 <c>[ExtensionEndpointRegistrations]</c> 程序集特性——禁止对本接口调用 WebExtension <c>MapGenerated&lt;T&gt;()</c>（启动 fail-closed）；端点映射仅在主包 <c>IPasswordLoginService</c>。</remarks>
/// </summary>
public interface IPasswordLoginServiceContract : IDomainService
{
    /// <summary>密码登录（凭据校验 → 查账号 → 签发令牌）——返回 TokenResponse；
    /// 前置频控命中抛 RateLimitedException（PASSWORD_RATE_LIMITED）。</summary>
    [AllowAnonymousFlag]
    Task<TokenResponse> LoginAsync(PasswordLoginRequest request, CancellationToken ct = default);
}
