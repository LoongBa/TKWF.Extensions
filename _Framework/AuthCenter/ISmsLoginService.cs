using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 短信验证码登录编排门面（V4.10.55 ADR92，T3 闭环——表现层零编排终态；B3b MapGenerated 生成化升级返回
/// <see cref="TokenResponse"/>——门面内完成 认证→取账号→签发 全编排，端点只调一个门面；失败抛
/// <see cref="TKW.Framework.Domain.Exceptions.DomainException"/>（400，消息 = 语义码），异常直达中间件无 handler 映射）。
/// <para>控制器不得 <c>[FromServices] IEnumerable&lt;IAuthenticationProvider&gt;</c> 直注集合（帧外枚举抛守卫）——
/// 改经本门面 <c>User.Use&lt;ISmsLoginService&gt;().LoginAsync(phone, code)</c> 编排（EduPlatform 3 端点修复配套）。</para>
/// <para>选区：内部按 <see cref="AuthTypes.Sms"/> 从 <c>IEnumerable&lt;IAuthenticationProvider&gt;</c> 集合挑选
/// Provider（门面 ctor 注入集合——经 <c>User.Use&lt;门面&gt;()</c> 帧内枚举，守卫工厂经 CurrentAopUser 供给）。</para>
/// </summary>
public interface ISmsLoginService : IDomainService
{
    /// <summary>短信验证码登录（验证码校验 → 有账号登录 / 无账号建账号 → 签发令牌）——返回 <see cref="TokenResponse"/>。
    /// 失败抛 <see cref="TKW.Framework.Domain.Exceptions.DomainException"/>（SMS_PROVIDER_NOT_ENABLED / 认证失败语义码 / ACCOUNT_NOT_FOUND）。
    /// <paramref name="request"/>：<c>{phone, code}</c>（body 绑定）。</summary>
    [AllowAnonymousFlag]
    [ApiExpose(RestRoute = "/login/sms", RestMethod = "POST", AllowAnonymous = true)]
    Task<TokenResponse> LoginAsync(SmsLoginRequest request, CancellationToken ct = default);
}