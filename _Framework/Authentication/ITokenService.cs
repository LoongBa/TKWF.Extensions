using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Authentication;

/// <summary>签发请求——认证成功后由认证流程构造（方案 §5.4）。</summary>
/// <param name="UserId">平台内部 id（JWT sub=user:{UserId}）。</param>
/// <param name="AuthType">认证方式（AuthTypes 常量）。</param>
/// <param name="AuthLevel">认证强度（AuthLevel 枚举值）。</param>
/// <param name="TeacherVerified">教师核实声明（身份声明，非业务角色）。</param>
/// <param name="DeviceInfo">签发设备信息（可选，随 Refresh Token 落库）。</param>
public sealed record TokenIssueRequest(
    string UserId,
    string AuthType,
    int AuthLevel,
    bool TeacherVerified,
    string? DeviceInfo = null);

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
/// <param name="TeacherVerified">教师核实声明。</param>
/// <param name="Jti">令牌唯一 id（撤销/黑名单关联键）。</param>
/// <param name="ExpiresAtUtc">过期时间（UTC）。</param>
/// <param name="Claims">令牌载荷完整声明（iss/sub/iat/kid 等，供 IAuthorizationMapper 消费）。</param>
public sealed record TokenValidationResult(
    string UserId,
    string AuthType,
    int AuthLevel,
    bool TeacherVerified,
    string Jti,
    DateTime ExpiresAtUtc,
    IReadOnlyDictionary<string, string> Claims);

/// <summary>
/// 令牌服务契约——签发/验证/刷新/撤销（业界成熟重写：持久化密钥 + 黑名单落库 + Refresh rotation + TokenVersion 闭环）。
/// <para>方案 §5.4——摒弃 DMP 三缺陷：内存 Lazy&lt;RSA&gt; 密钥 / 内存 ConcurrentDictionary 黑名单 / 直注入 IEntityDAC。</para>
/// </summary>
public interface ITokenService
{
    /// <summary>签发 access + refresh 令牌对（RSA RS256 + kid + jti + exp/iat）。</summary>
    Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, CancellationToken ct = default);

    /// <summary>验证 access token——验签（alg 强制 RS256 + FixedTimeEquals）+ kid 白名单 + exp/iat + iss + 黑名单 → 载荷。</summary>
    Task<TokenValidationResult> ValidateTokenAsync(string accessToken, CancellationToken ct = default);

    /// <summary>刷新令牌 rotation——SHA256 查旧 → TokenVersion 校验 → 置 revoked → 签发新对；重用已撤销 → 全撤销 + Warning。</summary>
    Task<TokenRefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>撤销 access token——按 jti 落黑名单（ExpiresAt = 该 token 自然过期时间）。</summary>
    Task RevokeTokenAsync(string jti, string reason, CancellationToken ct = default);
}
