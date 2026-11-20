using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 密码登录契约基接口（V0.3.0 契约包提取——业务签名进契约包，端点面留主包门面）。
/// <para>鉴权语义：先认证后断言；本契约不提供裸凭据校验原语（B2 安全约定）。</para>
/// </summary>
public interface IPasswordLoginServiceContract : IDomainService
{
    /// <summary>密码登录（凭据校验 → 查账号 → 签发令牌）——返回 TokenResponse；
    /// 前置频控命中抛 RateLimitedException（PASSWORD_RATE_LIMITED）。</summary>
    [AllowAnonymousFlag]
    Task<TokenResponse> LoginAsync(PasswordLoginRequest request, CancellationToken ct = default);
}
