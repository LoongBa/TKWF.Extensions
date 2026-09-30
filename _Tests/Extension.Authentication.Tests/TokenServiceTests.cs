using System;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>
/// TokenService 测试——D2 签发载荷契约 / D3 验签+exp+kid+黑名单 / D4 Refresh rotation+TokenVersion+重用检测 / D5 密钥 fail-fast。
/// </summary>
public class TokenServiceTests
{
    private static (TokenService Service, IFreeSql Fsql) CreateService(AuthCenterOptions options)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var accountDs = new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)));
        var refreshDs = new AuthRefreshTokenEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthRefreshTokenEntity>(new UnitOfWorkManager(fsql)));
        var blacklistDs = new AuthTokenBlacklistEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthTokenBlacklistEntity>(new UnitOfWorkManager(fsql)));
        var service = new TokenService(
            Options.Create(options), accountDs, refreshDs, blacklistDs,
            new MemoryCache(new MemoryCacheOptions()), NullLogger<TokenService>.Instance);
        return (service, fsql);
    }

    private static AuthAccountEntity CreateAccount(string? phone = "13800138000", int authLevel = 1)
        => new()
        {
            UId = AccountIdGenerator.NewUId(),
            Phone = phone,
            AuthLevel = authLevel,
            TokenVersion = 0
        };

    // ── D2 签发载荷契约 ──────────────────────────────────────────────

    [Fact]
    public async Task IssueToken_Payload_MatchesFrozenContract()
    {
        var (service, fsql) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)))
            .CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));

        var parts = result.AccessToken.Split('.');
        Assert.Equal(3, parts.Length);
        var payload = JsonDocument.Parse(Decode(parts[1])).RootElement;

        Assert.Equal("user:" + account.UId, payload.GetProperty("sub").GetString());
        Assert.Equal(account.UId, payload.GetProperty("userId").GetString());
        Assert.Equal(AuthTypes.Sms, payload.GetProperty("authType").GetString());
        Assert.Equal((int)AuthLevel.Phone, payload.GetProperty("auth_level").GetInt32());
        Assert.False(payload.GetProperty("teacher_verified").GetBoolean());
        Assert.True(payload.TryGetProperty("exp", out _) && payload.TryGetProperty("iat", out _));
        Assert.True(payload.TryGetProperty("jti", out _) && !string.IsNullOrEmpty(payload.GetProperty("jti").GetString()));
        Assert.Equal("test-key-1", payload.GetProperty("kid").GetString());
        // 无业务角色（冻结契约——角色不进令牌）
        Assert.False(payload.TryGetProperty("roles", out _));
        Assert.False(payload.TryGetProperty("role", out _));

        // Refresh 落库（SHA256——DB 无明文）
        var refreshDs = new AuthRefreshTokenEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthRefreshTokenEntity>(new UnitOfWorkManager(fsql)));
        var row = await refreshDs.GetByTokenHashAsync(TokenService.Sha256Hex(result.RefreshToken));
        Assert.NotNull(row);
        Assert.Equal(account.UId, row!.UserId);
        Assert.Equal(0, row.TokenVersion);
    }

    [Fact]
    public async Task IssueToken_RefreshToken_RawNotStoredInDb()
    {
        var (service, fsql) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)))
            .CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));

        var refreshDs = new AuthRefreshTokenEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthRefreshTokenEntity>(new UnitOfWorkManager(fsql)));
        // 明文 refresh 不应作为 TokenHash 出现
        var rawRow = await refreshDs.GetByTokenHashAsync(TokenService.Sha256Hex(result.RefreshToken));
        Assert.NotNull(rawRow);
        Assert.NotEqual(result.RefreshToken, rawRow!.TokenHash);
    }

    // ── D3 验签 + exp + kid + 黑名单 ─────────────────────────────────

    [Fact]
    public async Task ValidateToken_ValidToken_Passes()
    {
        var (service, fsql) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)))
            .CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, true));
        var validation = await service.ValidateTokenAsync(result.AccessToken);

        Assert.Equal(account.UId, validation.UserId);
        Assert.Equal(AuthTypes.Sms, validation.AuthType);
        Assert.Equal((int)AuthLevel.Phone, validation.AuthLevel);
        Assert.True(validation.TeacherVerified);
        Assert.True(validation.Claims.ContainsKey("iss"));
    }

    [Fact]
    public async Task ValidateToken_TamperedSignature_Rejected()
    {
        var (service, fsql) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)))
            .CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));
        // ⚠️ 篡改末组第一字符（X 位）而非末位字符——末位是 base64url 对的第二字符（Y 位），其低 4 位被解码丢弃，
        //    末位 A↔B 翻转可能落入 padding 位（解码逐字节相同 → 验签合法通过 → 测试 ~25% 概率误报失败）。
        var tampered = result.AccessToken[..^2] + (result.AccessToken[^2] == 'A' ? 'B' : 'A') + result.AccessToken[^1];

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ValidateTokenAsync(tampered));
        Assert.Equal("INVALID_SIGNATURE", ex.Message);
    }

    [Fact]
    public async Task ValidateToken_AlgNone_Forged_Rejected()
    {
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        // 构造 alg=none 伪造令牌（header {alg:none} + 空签名）
        var header = TokenService.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var payload = TokenService.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"sub\":\"user:u-attacker\",\"exp\":9999999999}"));
        var forged = $"{header}.{payload}.";

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ValidateTokenAsync(forged));
        Assert.Equal("ALG_NOT_ALLOWED", ex.Message);
    }

    [Fact]
    public async Task ValidateToken_Expired_Rejected()
    {
        var options = AuthenticationTestHost.CreateOptions();
        options.AccessTokenExpirationMinutes = -5; // 已过期
        var (service, fsql) = CreateService(options);
        var account = CreateAccount();
        await new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)))
            .CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ValidateTokenAsync(result.AccessToken));
        Assert.Equal("TOKEN_EXPIRED", ex.Message);
    }

    [Fact]
    public async Task ValidateToken_UnknownKid_Rejected()
    {
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        // 用未知 kid 伪造 header
        var header = TokenService.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\",\"kid\":\"attacker-kid\"}"));
        var payload = TokenService.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"sub\":\"user:u-x\",\"exp\":9999999999}"));
        var forged = $"{header}.{payload}.AAAA";

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ValidateTokenAsync(forged));
        Assert.Equal("KID_UNKNOWN", ex.Message);
    }

    [Fact]
    public async Task ValidateToken_Revoked_Rejected()
    {
        var (service, fsql) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)))
            .CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));
        var validation = await service.ValidateTokenAsync(result.AccessToken);

        await service.RevokeTokenAsync(validation.Jti, "logout");
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ValidateTokenAsync(result.AccessToken));
        Assert.Equal("TOKEN_REVOKED", ex.Message);
    }

    // ── D4 Refresh rotation + TokenVersion + 重用检测 ────────────────

    [Fact]
    public async Task RefreshToken_Rotation_OldRevoked_NewPairIssued()
    {
        var (service, fsql) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        var accountDs = new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)));
        await accountDs.CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));
        var oldRefresh = result.RefreshToken;

        var refreshed = await service.RefreshTokenAsync(oldRefresh);
        Assert.NotEqual(oldRefresh, refreshed.RefreshToken);

        // 旧 refresh 已撤销——重用 → 检测泄露
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.RefreshTokenAsync(oldRefresh));
        Assert.Equal("REFRESH_REUSED", ex.Message);
    }

    [Fact]
    public async Task RefreshToken_TokenVersionMismatch_Rejected()
    {
        var (service, fsql) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        var accountDs = new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)));
        await accountDs.CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));

        // 密码/绑定变更 → TokenVersion++（闭环）
        await accountDs.IncrementTokenVersionAsync(account.UId);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.RefreshTokenAsync(result.RefreshToken));
        Assert.Equal("REFRESH_STALE", ex.Message);
    }

    [Fact]
    public async Task RefreshToken_ReuseDetected_RevokesAllForUser()
    {
        var (service, fsql) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        var accountDs = new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)));
        await accountDs.CreateAsync(account);

        // 签发两个 refresh（同账号）
        var r1 = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));
        var r2 = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));

        // 正常刷新 r2（旧 r2 变 revoked）
        await service.RefreshTokenAsync(r2.RefreshToken);
        // 重用旧 r2 → 泄露 → 撤销该用户全部 refresh（含 r1）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.RefreshTokenAsync(r2.RefreshToken));
        Assert.Equal("REFRESH_REUSED", ex.Message);

        var refreshDs = new AuthRefreshTokenEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthRefreshTokenEntity>(new UnitOfWorkManager(fsql)));
        // r1 也应被撤销（清场）
        var r1Row = await refreshDs.GetByTokenHashAsync(TokenService.Sha256Hex(r1.RefreshToken));
        Assert.NotNull(r1Row);
        Assert.True(r1Row!.IsRevoked);
    }

    // ── D5 密钥 fail-fast ─────────────────────────────────────────────

    [Fact]
    public void Production_MissingSigningKey_FailFast()
    {
        var options = new AuthCenterOptions { IsProduction = true, Issuer = "auth-test" };
        var (service, _) = CreateService(options);
        Assert.Throws<InvalidOperationException>(() => service.EnsureKeysLoaded());
    }

    [Fact]
    public void Production_MissingIssuer_FailFast()
    {
        var options = AuthenticationTestHost.CreateOptions();
        options.Issuer = "";
        options.IsProduction = true;
        var (service, _) = CreateService(options);
        Assert.Throws<InvalidOperationException>(() => service.EnsureKeysLoaded());
    }

    [Fact]
    public void Dev_MissingSigningKey_AutoGeneratesTemporary()
    {
        var options = new AuthCenterOptions { IsProduction = false, Issuer = "auth-test" };
        var (service, fsql) = CreateService(options);
        var account = CreateAccount();
        new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)))
            .CreateAsync(account).GetAwaiter().GetResult();

        var result = service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false))
            .GetAwaiter().GetResult();
        Assert.Equal(3, result.AccessToken.Split('.').Length);
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static string Decode(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        s += new string('=', (4 - s.Length % 4) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
