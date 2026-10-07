using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interception.Filters;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>认证上下文——Provider 输入（方案 §5.5：认证矩阵多方式）。</summary>
/// <param name="Phone">手机号（sms）。</param>
/// <param name="Code">短信验证码（sms）。</param>
/// <param name="WechatCode">微信授权 code（wechat）。</param>
/// <param name="WechatScope">微信授权 scope：snsapi_base（网页）/snsapi_login（扫码）。</param>
/// <param name="Password">密码（password Provider——V0.9.0 SecurePassword：客户端算 clientHash+salt，服务端零明文）。</param>
/// <param name="DeviceInfo">设备信息（可选，透传签发）。</param>
/// <param name="Identifier">凭据标识（password Provider——UId 或 Phone，V0.9.0 B.9 新增；仅密码链路使用）。</param>
/// <param name="ClientHash">客户端算的密码散列（hex(PBKDF2(password, salt, iterations, 32bytes))——SecurePassword 协议，V0.9.0 ADR 决策 1）。</param>
/// <param name="Salt">客户端算的盐（hex(salt 32bytes)——SecurePassword 协议；与 <see cref="ClientHash"/> 配套）。</param>
public sealed record ProviderAuthenticateContext(
    string? Phone = null,
    string? Code = null,
    string? WechatCode = null,
    string? WechatScope = null,
    string? Password = null,
    string? DeviceInfo = null,
    string? Identifier = null,
    string? ClientHash = null,
    string? Salt = null);

/// <summary>认证结果（方案 §5.5）。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="UserId">成功后平台内部 id（失败 null）。</param>
/// <param name="FailReason">失败原因（供 AuthLoginAttempt 落库）。</param>
/// <param name="AuthLevel">认证强度（默认手机号）。</param>
public sealed record ProviderAuthenticateResult(
    bool Success,
    string? UserId,
    string? FailReason,
    AuthLevel AuthLevel = AuthLevel.Phone);

/// <summary>
/// 认证 Provider 契约——认证矩阵多方式（配置化启用，fail-closed）。
/// <para>方案 §5.5——<c>AuthCenterOptions.EnabledAuthTypes</c> 启用集合之外 Provider 不接线；</para>
/// <para>内置：<c>SmsAuthenticationProvider</c>（短信验证码）+ <c>WeChatAuthenticationProvider</c>（微信双形态）；密码/抖音为扩展点。</para>
/// </summary>
public interface IAuthenticationProvider : IDomainService
{
    /// <summary>认证方式标识（AuthTypes 常量）。</summary>
    string AuthType { get; }

    /// <summary>执行认证——成功返回平台内部 id；失败返回 FailReason。</summary>
    /// <remarks>匿名面声明：短信/微信登录与 OAuth 回调为无会话入口（EduPlatform 转达 E1）。</remarks>
    [AllowAnonymousFlag]
    Task<ProviderAuthenticateResult> AuthenticateAsync(ProviderAuthenticateContext context, CancellationToken ct = default);
}
