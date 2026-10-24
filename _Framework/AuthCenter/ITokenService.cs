using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
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
    /// <remarks>匿名面声明：登录签发为无会话入口（EduPlatform 转达 E1）。<c>ExposeRest=false</c>：签发为认证流程内部入口，不生成端点。</remarks>
    [AllowAnonymousFlag]
    [ApiExpose(ExposeRest = false)]
    Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, CancellationToken ct = default);

    /// <summary>验证 access token——验签（alg 强制 RS256 + FixedTimeEquals）+ kid 白名单 + exp/iat + iss + 黑名单 → 载荷。
    /// <c>ExposeRest=false</c>：验签为身份适配层内部入口（中间件/UserHelper 消费），不生成端点。</summary>
    [ApiExpose(ExposeRest = false)]
    Task<TokenValidationResult> ValidateTokenAsync(string accessToken, CancellationToken ct = default);

    /// <summary>刷新令牌 rotation——SHA256 查旧 → TokenVersion 校验 → 置 revoked → 签发新对；重用已撤销 → 全撤销 + Warning。
    /// 失败抛 <see cref="TKW.Framework.Domain.Exceptions.DomainException"/>（400，消息 = 原语义码如 REFRESH_NOT_FOUND/REFRESH_EXPIRED/ACCOUNT_FROZEN）。
    /// <paramref name="request"/>：<c>{refresh_token|refreshToken}</c>（body 绑定——双收别名保留）。</summary>
    /// <remarks>匿名面声明：Refresh 自携带凭据（EduPlatform 转达 E1）。</remarks>
    [AllowAnonymousFlag]
    [ApiExpose(RestRoute = "/refresh", RestMethod = "POST", AllowAnonymous = true)]
    Task<TokenResponse> RefreshTokenAsync(RefreshRequest request, CancellationToken ct = default);

    /// <summary>撤销 access token——按 jti 落黑名单（ExpiresAt = 该 token 自然过期时间）。
    /// <c>ExposeRest=false</c>：撤销为内部能力（/logout 经 <see cref="RevokeCurrentSessionAsync"/> 生成端点按 ambient 撤销）。</summary>
    /// <remarks>匿名面声明：登出按 jti 自携带凭据自校验（EduPlatform 转达 E1）。</remarks>
    [AllowAnonymousFlag]
    [ApiExpose(ExposeRest = false)]
    Task RevokeTokenAsync(string jti, string reason, CancellationToken ct = default);

    /// <summary>
    /// 当前请求 Bearer 验签产物（Oracle5 2026-10-24 ambient 方案 A——/verify 内省数据源）。
    /// 经框架 <see cref="TKW.Framework.Domain.Web.Authentication.ITokenVerificationContext"/> 读中间件写入的
    /// 当前令牌 jti/userId/authType/exp；请求非 Bearer 认证时 null → 抛
    /// <see cref="System.Security.Authentication.AuthenticationException"/>（401 AUTH_REQUIRED）。
    /// </summary>
    [AllowAnonymousFlag]
    [ApiExpose(RestRoute = "/verify", RestMethod = "GET")]   // AllowAnonymous=false（默认省略）——已认证端点，Bearer 必需（ RequireAuthorization）
    Task<VerifyResponse> GetCurrentTokenInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// 撤销当前请求的 Bearer 令牌（Oracle5 2026-10-24 ambient 方案 A——/logout 撤销数据源）。
    /// jti 取自 ambient（不经请求体——Web 层零编排），reason 固定 "logout"；ambient null（非 Bearer 认证）→
    /// 抛 <see cref="System.Security.Authentication.AuthenticationException"/>（401 AUTH_REQUIRED）。
    /// </summary>
    [AllowAnonymousFlag]
    [ApiExpose(RestRoute = "/logout", RestMethod = "POST")]   // AllowAnonymous=false（默认省略）——已认证端点，Bearer 必需（RequireAuthorization）
    Task RevokeCurrentSessionAsync(CancellationToken ct = default);
}
