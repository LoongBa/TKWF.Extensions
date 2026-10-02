using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 令牌验证器契约——本地公钥验签 / 远程 introspection 二选一（Options.VerifyMode）。
/// <para>方案 §5.8——替代 DMP 仅接口无实现的 <c>ISessionExchangeService</c>（主框架/扩展生态均不存在）；</para>
/// <para>供 <see cref="JwtDomainUserParser.ParseAndVerifyAsync"/> 与 <see cref="IAuthorizationMapper{TUserInfo}"/> 消费。</para>
/// </summary>
public interface ITokenVerifier : IDomainService
{
    /// <summary>验证令牌——失败抛 <see cref="System.Security.Authentication.AuthenticationException"/>（对齐框架登录失败语义）。</summary>
    Task<TokenValidationResult> VerifyAsync(string token, CancellationToken ct = default);
}
