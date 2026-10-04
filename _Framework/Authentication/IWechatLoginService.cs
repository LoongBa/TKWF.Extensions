using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 微信 OAuth 登录编排门面（V4.10.55 ADR92，T3 闭环——表现层零编排终态）。
/// <para>控制器不得 <c>[FromServices] IEnumerable&lt;IAuthenticationProvider&gt;</c> 直注集合（帧外枚举抛守卫）——
/// 改经本门面 <c>User.Use&lt;IWechatLoginService&gt;().LoginAsync(wechatCode, wechatScope)</c> 编排（EduPlatform 3 端点修复配套）。</para>
/// <para>选区：内部按 <see cref="AuthTypes.Wechat"/> 从 <c>IEnumerable&lt;IAuthenticationProvider&gt;</c> 集合挑选
/// Provider（门面 ctor 注入集合——经 <c>User.Use&lt;门面&gt;()</c> 帧内枚举，守卫工厂经 CurrentAopUser 供给）。
/// WechatScope：snsapi_base（网页）/snsapi_login（扫码）——透传 <see cref="ProviderAuthenticateContext"/>。</para>
/// </summary>
public interface IWechatLoginService : IDomainService
{
    /// <summary>微信 OAuth 登录（code 换 openid → 查/建账号并绑定）——返回 <see cref="ProviderAuthenticateResult"/>。</summary>
    [AllowAnonymousFlag]
    Task<ProviderAuthenticateResult> LoginAsync(string wechatCode, string wechatScope, CancellationToken ct = default);
}