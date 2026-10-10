using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>签发请求——认证成功后由认证流程构造（方案 §5.4）。</summary>
/// <param name="UserId">平台内部 id（JWT sub=user:{UserId}）。</param>
/// <param name="AuthType">认证方式（AuthTypes 常量）。</param>
/// <param name="AuthLevel">认证强度（AuthLevel 枚举值）。</param>
/// <param name="DeviceInfo">签发设备信息（可选，随 Refresh Token 落库）。</param>
/// <param name="ChannelType">渠道类型（T5 三层边界 2026-10-09——外部 IdP 登录时填平台库 channelType 如
/// <c>wechat_oauth</c>/<c>qq_oauth</c>，JWT 写 <c>channel_type</c> claim；非空时写，null 不写。末位可选参数默认 null——零破坏既有调用）。</param>
public sealed record TokenIssueRequest(
    string UserId,
    string AuthType,
    int AuthLevel,
    string? DeviceInfo = null,
    string? ChannelType = null);

/// <summary>签发结果（方案 §5.4）。</summary>
/// <param name="AccessToken">JWT access token（RS256）。</param>
/// <param name="RefreshToken">刷新令牌（明文仅返回一次，落库存 SHA256）。</param>
/// <param name="ExpiresIn">access token 有效秒数。</param>
public sealed record TokenIssueResult(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>刷新结果（方案 §5.4——rotation：旧 refresh 置 revoked + 新对签发）。</summary>
public sealed record TokenRefreshResult(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>令牌验证结果（方案 §5.4/§5.8——验签 + kid + exp + 黑名单 → 载荷）。</summary>
/// <param name="UserId">平台内部 id（sub）。</param>
/// <param name="AuthType">认证方式。</param>
/// <param name="AuthLevel">认证强度。</param>
/// <param name="Jti">令牌唯一 id（撤销/黑名单关联键）。</param>
/// <param name="ExpiresAtUtc">过期时间（UTC）。</param>
/// <param name="Claims">令牌载荷完整声明（iss/sub/iat/kid 等，供 IAuthorizationMapper 消费）。</param>
/// <param name="ChannelType">渠道类型（T5 2026-10-09——JWT <c>channel_type</c> claim 回读；外部 IdP 登录 token 非空，其余 null）。</param>
public sealed record TokenValidationResult(
    string UserId,
    string AuthType,
    int AuthLevel,
    string Jti,
    DateTime ExpiresAtUtc,
    IReadOnlyDictionary<string, string> Claims,
    string? ChannelType = null);

/// <summary>
/// 令牌服务契约——签发/验证/刷新/撤销（业界成熟重写：持久化密钥 + 黑名单落库 + Refresh rotation + TokenVersion 闭环）。
/// <para>方案 §5.4——摒弃 DMP 三缺陷：内存 Lazy&lt;RSA&gt; 密钥 / 内存 ConcurrentDictionary 黑名单 / 直注入 IEntityDAC。</para>
/// <para>V0.3.1（A' 裁定）：继承 <see cref="IDomainService"/> 空标记——领域服务体系准入（<c>Use&lt;T&gt;() where T : IDomainService</c>
/// 泛型约束 + SystemActor 调用链；启动预检经 <c>scope.System.Use&lt;ITokenService&gt;()</c> 解析，自治铁律零妥协）。</para>
/// </summary>
public interface ITokenService : IDomainService
{
    /// <summary>签发 access + refresh 令牌对（RSA RS256 + kid + jti + exp/iat）。</summary>
    /// <remarks>匿名面声明：登录签发为无会话入口（EduPlatform 转达 E1）。</remarks>
    [AllowAnonymousFlag]
    Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, CancellationToken ct = default);

    /// <summary>验证 access token——验签（alg 强制 RS256 + FixedTimeEquals）+ kid 白名单 + exp/iat + iss + 黑名单 → 载荷。</summary>
    Task<TokenValidationResult> ValidateTokenAsync(string accessToken, CancellationToken ct = default);

    /// <summary>刷新令牌 rotation——SHA256 查旧 → TokenVersion 校验 → 置 revoked → 签发新对；重用已撤销 → 全撤销 + Warning。</summary>
    /// <remarks>匿名面声明：Refresh 自携带凭据（EduPlatform 转达 E1）。</remarks>
    [AllowAnonymousFlag]
    Task<TokenRefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>撤销 access token——按 jti 落黑名单（ExpiresAt = 该 token 自然过期时间）。</summary>
    /// <remarks>匿名面声明：登出按 jti 自携带凭据自校验（EduPlatform 转达 E1）。</remarks>
    [AllowAnonymousFlag]
    Task RevokeTokenAsync(string jti, string reason, CancellationToken ct = default);
}
