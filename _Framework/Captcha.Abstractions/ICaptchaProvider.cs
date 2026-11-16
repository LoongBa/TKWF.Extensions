using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Captcha;

/// <summary>
/// 人机验证挑战（方案 §5.1——Payload = Provider 自渲染不透明串，锁形状不锁媒介；答案永不进客户端载荷）。
/// </summary>
/// <param name="ChallengeId">挑战唯一标识（VerifyAsync 回传键）。</param>
/// <param name="Payload">Provider 自渲染的展示载荷（如图形码 SVG 串 / 第三方验证初始化数据）——不透明。</param>
/// <param name="TtlSeconds">挑战有效期（秒，Provider 声明的实际 TTL）。</param>
public sealed record CaptchaChallenge(string ChallengeId, string Payload, int TtlSeconds);

/// <summary>
/// 人机验证结果（方案 §5.1——score 可选：评分型 Provider 门面按场景阈值裁决）。
/// </summary>
/// <param name="Passed">验证是否通过（恒死锁：未通过一律 false，不区分判因防枚举）。</param>
/// <param name="Score">评分型 Provider 的置信度（0~1，可选——v1 本地图形 Provider 恒 null）。</param>
public sealed record CaptchaVerifyResult(bool Passed, double? Score = null);

/// <summary>
/// 人机验证 Provider SPI（方案 §5.1——v1 冻结形状）。
/// <para>职责边界：Provider 只做"生成/校验"（挑战态生命周期由 Provider 自持——v1 本地图形 Provider 经
/// IMemoryCache 存答案散列 + TTL/尝试上限/单次消费）；门面（<see cref="ICaptchaService"/>）负责
/// Provider 选中（Options <c>EnabledProviderType</c>）+ Issue IP 频控 + fail-closed 裁决。</para>
/// <para>多 Provider：Initializer <c>TryAddEnumerableConstructible</c> 注册（ADR92 集合版守卫工厂），
/// 实现继承 <see cref="TKW.Framework.Domain.DomainServiceBase"/>；消费方在 <c>User.Use&lt;门面&gt;()</c> 帧内
/// 经门面间接枚举（禁帧外 [FromServices] 集合预绑定）。</para>
/// </summary>
public interface ICaptchaProvider : IDomainService
{
    /// <summary>Provider 类型标识（"image" / "geetest" / "tencent" / ...——Options EnabledProviderType 精确匹配）。</summary>
    string Type { get; }

    /// <summary>签发新挑战（侧效应：Provider 自持挑战态——答案散列/TTL/尝试计数）。</summary>
    Task<CaptchaChallenge> IssueAsync(string scene, CancellationToken ct = default);

    /// <summary>校验挑战作答（单次消费；失败不区分判因——统一 Passed=false）。</summary>
    Task<CaptchaVerifyResult> VerifyAsync(string challengeId, string answer, CancellationToken ct = default);
}