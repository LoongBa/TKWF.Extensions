using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SSO;

/// <summary>token2 签发请求。</summary>
/// <param name="UId">平台内部 id（sub）。</param>
/// <param name="TargetAppId">目标应用 id（aud）。</param>
/// <param name="Scope">该应用注册范围（scope）。</param>
public sealed record Token2IssueRequest(string UId, string TargetAppId, string? Scope);

/// <summary>token2 验证结果。</summary>
/// <param name="UId">sub（平台内部 id）。</param>
/// <param name="TargetAppId">aud（目标应用 id）。</param>
/// <param name="Jti">令牌唯一 id。</param>
/// <param name="ExpiresAtUtc">过期时间。</param>
/// <param name="Claims">载荷声明（scope 等）。</param>
public sealed record Token2ValidationResult(string UId, string TargetAppId, string Jti, DateTime ExpiresAtUtc, IReadOnlyDictionary<string, string> Claims);

/// <summary>token2 签发结果。</summary>
/// <param name="Token">ES256 JWT。</param>
/// <param name="ExpiresIn">有效秒数。</param>
public sealed record Token2IssueResult(string Token, int ExpiresIn);

/// <summary>
/// token2 服务——手写 ES256 JWT 签发/验证（联邦层统一断言）。
/// <para>独立密钥域（ADR-SSO Oracle P2-5）：ECDSA P-256 PEM 持久化 + kid 轮换 + JWKS 分发；
/// 与 Authentication RS256 密钥域完全独立（算法/iss/消费方不同，不复用既有 TokenService）。</para>
/// <para>🔒 验签安全加固（对齐 Authentication Oracle C1）：alg 强制 ES256 / 曲线强制 P-256 /
/// exp·iat leeway / kid 白名单 / iss 校验 / Base64Url 边界。</para>
/// </summary>
public interface IToken2Service : IDomainService
{
    /// <summary>签发 token2（ES256 自包含 JWT）。</summary>
    Task<Token2IssueResult> IssueToken2Async(Token2IssueRequest request, CancellationToken ct = default);

    /// <summary>验签 token2（alg/曲线/kid/iss/exp·iat + leeway → 载荷；失败抛异常含机器可读原因码）。</summary>
    Task<Token2ValidationResult> ValidateToken2Async(string token, CancellationToken ct = default);

    /// <summary>JWKS 序列化（公开分发——kid + x/y 坐标，RFC 7517）。</summary>
    string GetJwksJson();
}
