using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 短信验证码登录编排门面（V4.10.55 ADR92，T3 闭环——表现层零编排终态）。
/// <para>控制器不得 <c>[FromServices] IEnumerable&lt;IAuthenticationProvider&gt;</c> 直注集合（帧外枚举抛守卫）——
/// 改经本门面 <c>User.Use&lt;ISmsLoginService&gt;().LoginAsync(phone, code)</c> 编排（EduPlatform 3 端点修复配套）。</para>
/// <para>选区：内部按 <see cref="AuthTypes.Sms"/> 从 <c>IEnumerable&lt;IAuthenticationProvider&gt;</c> 集合挑选
/// Provider（门面 ctor 注入集合——经 <c>User.Use&lt;门面&gt;()</c> 帧内枚举，守卫工厂经 CurrentAopUser 供给）。
/// 失败语义透传 <see cref="ProviderAuthenticateResult"/>（Success=false + FailReason——装配层负责落库/HTTP 映射）。</para>
/// </summary>
public interface ISmsLoginService : IDomainService
{
    /// <summary>短信验证码登录（验证码校验 → 有账号登录 / 无账号建账号）——返回 <see cref="ProviderAuthenticateResult"/>。</summary>
    [AllowAnonymousFlag]
    Task<ProviderAuthenticateResult> LoginAsync(string phone, string code, CancellationToken ct = default);
}