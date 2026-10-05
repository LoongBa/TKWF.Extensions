using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>accesscode 签发请求（通道 B：公众号后端 → SSO Issue API 内部）。</summary>
/// <param name="ChannelId">channel 实例 id（微信 = 公众号 id）。</param>
/// <param name="UId">平台内部 id（查/建 uid 后）。</param>
/// <param name="TargetAppId">目标应用 id（token2 aud）。</param>
/// <param name="Scope">该应用注册范围（签发 token2 时注入）。</param>
/// <param name="OpenId">渠道 openid（仅审计——不出 token2/URL）。</param>
/// <param name="CodeVerifier">PKCE code_verifier（可选——存 SHA256 hash，Oracle P2-1）。</param>
/// <param name="IpAddress">来源 IP（审计）。</param>
public sealed record SsoAccessCodeIssueRequest(
    string ChannelId, string UId, string TargetAppId, string? Scope,
    string? OpenId = null, string? CodeVerifier = null, string? IpAddress = null);

/// <summary>accesscode 签发结果。</summary>
/// <param name="Code">base64url 32 字节随机（唯一原文——库内只存 SHA256 hash）。</param>
/// <param name="ExpiresIn">有效秒数（默认 120）。</param>
public sealed record SsoAccessCodeIssueResult(string Code, int ExpiresIn);

/// <summary>accesscode 消费结果（通道 B：用户点击图文链接 → /sso/auth）。</summary>
/// <param name="UId">平台内部 id（token2 sub）。</param>
/// <param name="TargetAppId">目标应用 id（token2 aud）。</param>
/// <param name="Scope">注册范围。</param>
public sealed record SsoAccessCodeConsumeResult(string UId, string TargetAppId, string? Scope);

/// <summary>
/// SSO 授权码服务（联邦 accesscode）——通道 B 票据。
/// <para>设计文档 §6.1 + Oracle P2-1/P2-2：CSPRNG 32 字节 base64url / TTL 120s / 单次原子 CAS /
/// 只存 SHA256(code) 索引 / PKCE 可选（defense in depth）。</para>
/// </summary>
public interface ISsoAccessCodeService : IDomainService
{
    /// <summary>签发 accesscode（生成高熵 code + SHA256 落库 + 120s TTL + 可选 PKCE hash）。</summary>
    Task<SsoAccessCodeIssueResult> IssueAsync(SsoAccessCodeIssueRequest request, CancellationToken ct = default);

    /// <summary>消费 accesscode（原子 CAS used=false→true；重放/过期/verifier 不匹配 → 抛异常含机器可读原因码）。</summary>
    Task<SsoAccessCodeConsumeResult> ConsumeAsync(string code, string? codeVerifier = null, string? ipAddress = null, CancellationToken ct = default);
}
