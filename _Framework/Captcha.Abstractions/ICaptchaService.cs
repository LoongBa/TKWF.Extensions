using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Captcha;

/// <summary>
/// 人机验证门面契约（方案 §5.3——只发/验挑战；"哪些场景要门禁"归消费方场景注册表，CAPTCHA 场景无关）。
/// <para>门面职责：Provider 选中（Options <c>EnabledProviderType</c> 精确匹配，未匹配 → fail-closed 守卫）+ 
/// Issue 取题 IP 频控（<c>captcha:issue:{ip}</c>，默认 10/min）+ 全局开关裁决（<c>Captcha.Enabled=false</c> →
/// 恒守卫异常，禁静默放行）+ 委托 Provider（挑战态生命周期归 Provider 自持）。</para>
/// <para>消费方经 <c>User.Use&lt;ICaptchaService&gt;()</c> 帧内解析（AddConstructibleService 注册，接口 : IDomainService
/// 准入）；匿名面（登录/注册等）经游客帧 <c>guest.Use&lt;ICaptchaService&gt;()</c>。</para>
/// </summary>
public interface ICaptchaService : IDomainService
{
    /// <summary>签发新挑战（Scene 透传 Provider——v1 纯透传，供评分型/场景化 Provider 未来消费）。</summary>
    Task<CaptchaChallenge> IssueAsync(string scene, CancellationToken ct = default);

    /// <summary>校验挑战作答（失败统一 Passed=false，不区分判因）。</summary>
    Task<CaptchaVerifyResult> VerifyAsync(string challengeId, string answer, CancellationToken ct = default);
}