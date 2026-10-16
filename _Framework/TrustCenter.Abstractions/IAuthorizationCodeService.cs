using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.TrustCenter;

/// <summary>OAuth authorization code issue request (SSO federation flow). Split-rename from AccessCodeIssueRequest (Face 2, ADR109 D4).</summary>
public sealed record AuthorizationCodeIssueRequest(
    string ChannelId, string UId, string TargetAppId, string? Scope,
    string? OpenId = null, string? CodeVerifier = null, string? IpAddress = null);

/// <summary>OAuth authorization code issue result.</summary>
public sealed record AuthorizationCodeIssueResult(string Code, int ExpiresIn);

/// <summary>OAuth authorization code consume result (grant context - token2 mint input).</summary>
public sealed record AuthorizationCodeConsumeResult(string UId, string TargetAppId, string? Scope);

/// <summary>
/// OAuth authorization code service (SSO federation flow - Face 2). Split-rename from IAccessCodeService (ADR109 D4).
/// Composition mode (framework ruling): ConsumeAsync internally = ticket consumption + PKCE post-verify + token2 mint side-effect.
/// </summary>
public interface IAuthorizationCodeService : IDomainService
{
    Task<AuthorizationCodeIssueResult> IssueAsync(AuthorizationCodeIssueRequest request, CancellationToken ct = default);
    Task<AuthorizationCodeConsumeResult> ConsumeAsync(string code, string? codeVerifier = null, string? ipAddress = null, CancellationToken ct = default);
    Task<int> CleanupExpiredAsync(int? retentionDays = null, CancellationToken ct = default);
}