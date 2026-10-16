using System;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.TrustCenter;

[DiContractIgnore]
internal sealed class AuthorizationCodeService : DomainServiceBase, IAuthorizationCodeService
{
    private AuthorizationCodeEntityDataService? _ds;
    private readonly IOptions<TrustCenterOptions> _o;
    private readonly ILogger<AuthorizationCodeService> _l;
    private AuthorizationCodeEntityDataService Ds => _ds ??= User.Use<AuthorizationCodeEntityDataService>();

    public AuthorizationCodeService(IDomainUser user, IOptions<TrustCenterOptions> o, ILogger<AuthorizationCodeService> l) : base(user)
    {
        _o = o ?? throw new ArgumentNullException(nameof(o));
        _l = l ?? throw new ArgumentNullException(nameof(l));
    }

    public async Task<AuthorizationCodeIssueResult> IssueAsync(AuthorizationCodeIssueRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = NewAuthCode();
        var expiresIn = Math.Max(_o.Value.AuthorizationCodeExpirationSeconds, 1);
        var now = DateTime.UtcNow;
        var e = new AuthorizationCodeEntity
        {
            CodeHash = Sha256Hex(code),
            ChannelId = request.ChannelId,
            TargetAppId = request.TargetAppId,
            UId = request.UId,
            Scope = request.Scope,
            ExpiresAt = now.AddSeconds(expiresIn),
            Used = false,
            IpAddress = request.IpAddress,
            CodeVerifierHash = !string.IsNullOrWhiteSpace(request.CodeVerifier) ? Sha256Hex(request.CodeVerifier) : null,
            CreateTime = now,
            UpdateTime = now,
        };
        await Ds.EntityCreateAsync(e, ct);
        return new AuthorizationCodeIssueResult(code, expiresIn);
    }

    public async Task<AuthorizationCodeConsumeResult> ConsumeAsync(string code, string? codeVerifier = null, string? ipAddress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");
        var now = DateTime.UtcNow;
        var row = await Ds.EntityGetAsync(m => m.CodeHash == Sha256Hex(code), ct);
        if (row == null) throw new AuthenticationException("ACCESS_CODE_NOT_FOUND");
        if (row.ExpiresAt <= now) throw new AuthenticationException("TICKET_EXPIRED");
        if (!string.IsNullOrEmpty(row.CodeVerifierHash))
        {
            if (string.IsNullOrEmpty(codeVerifier)) throw new AuthenticationException("TICKET_STATE_MISMATCH");
            if (!FixedTimeEquals(Sha256Hex(codeVerifier), row.CodeVerifierHash)) throw new AuthenticationException("TICKET_STATE_MISMATCH");
        }
        var affected = await Ds.EntityUpdateWhereAsync(m => m.Id == row.Id && !m.Used, m => new { Used = true }, ct);
        if (affected == 0)
        {
            _l.LogWarning("AuthCode replay (codeHash={CodeHash} uid={UId})", row.CodeHash, row.UId);
            throw new AuthenticationException("TICKET_CONSUMED");
        }
        return new AuthorizationCodeConsumeResult(row.UId, row.TargetAppId, row.Scope);
    }

    public async Task<int> CleanupExpiredAsync(int? retentionDays = null, CancellationToken ct = default)
    {
        var days = retentionDays ?? _o.Value.AuthorizationCodeRetentionDays;
        if (days <= 0) return 0;
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var stale = await Ds.EntitySelectAsync(m => m.ExpiresAt < cutoff, 0, 100_000, null, ct);
        if (stale.Count == 0) return 0;
        return await Ds.EntityDeleteBatchAsync(stale.Select(s => s.Id), ct);
    }

    private static string NewAuthCode() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Sha256Hex(string i) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(i)));
    private static string Base64UrlEncode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool FixedTimeEquals(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}