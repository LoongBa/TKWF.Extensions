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
using TKW.Framework.Utility.Caching;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// TokenService 测试——D2 签发载荷契约 / D3 验签+exp+kid+黑名单 / D4 Refresh rotation+TokenVersion+重用检测 / D5 密钥 fail-fast。
/// <para>V4.10.53（领域自治根治后重写）：门面继承 DomainServiceBase，DataService 经基类 User 懒加载（NoAop 路径）——
/// 测试用可配置 StubDomainUser 直构门面（经基类 User 取上下文），桩内 Use&lt;T&gt;() 按生产 NoAop 路径等价
/// （ActivatorUtilities 直建 DataService，IEntityDAC 从 DI 解析）。业务断言语义不变。</para>
/// </summary>
public class TokenServiceTests
{
    private static (TokenService Service, StubDomainUser Stub) CreateService(AuthCenterOptions options)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql); // 全部 8 实体 DAC——Use<DataService>() NoAop 直建源
        var service = new TokenService(
            stub, new DevKeyCache<TokenService.RsaKeySet>(), // E4 密钥管理抽象（V0.7.0）：dev 分支密钥缓存（单测内单实例自洽）
            Options.Create(options),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<TokenService>.Instance);
        return (service, stub);
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
        var (service, stub) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));

        var parts = result.AccessToken.Split('.');
        Assert.Equal(3, parts.Length);
        var payload = JsonDocument.Parse(Decode(parts[1])).RootElement;

        Assert.Equal("user:" + account.UId, payload.GetProperty("sub").GetString());
        Assert.Equal(account.UId, payload.GetProperty("userId").GetString());
        Assert.Equal(AuthTypes.Sms, payload.GetProperty("authType").GetString());
        Assert.Equal((int)AuthLevel.Phone, payload.GetProperty("auth_level").GetInt32());
        // V0.9.0（A.4）：teacher_verified claim 移除——令牌不再携带业务声明（教师核实走教育线业务扩展）
        Assert.False(payload.TryGetProperty("teacher_verified", out _));
        Assert.True(payload.TryGetProperty("exp", out _) && payload.TryGetProperty("iat", out _));
        Assert.True(payload.TryGetProperty("jti", out _) && !string.IsNullOrEmpty(payload.GetProperty("jti").GetString()));
        Assert.Equal("test-key-1", payload.GetProperty("kid").GetString());
        // 无业务角色（冻结契约——角色不进令牌）
        Assert.False(payload.TryGetProperty("roles", out _));
        Assert.False(payload.TryGetProperty("role", out _));

        // Refresh 落库（SHA256——DB 无明文）
        var refreshDs = stub.Use<AuthRefreshTokenEntityDataService>();
        var row = await refreshDs.GetByTokenHashAsync(TokenService.Sha256Hex(result.RefreshToken));
        Assert.NotNull(row);
        Assert.Equal(account.UId, row!.UserId);
        Assert.Equal(0, row.TokenVersion);
    }

    [Fact]
    public async Task IssueToken_RefreshToken_RawNotStoredInDb()
    {
        var (service, stub) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));

        var refreshDs = stub.Use<AuthRefreshTokenEntityDataService>();
        // 明文 refresh 不应作为 TokenHash 出现
        var rawRow = await refreshDs.GetByTokenHashAsync(TokenService.Sha256Hex(result.RefreshToken));
        Assert.NotNull(rawRow);
        Assert.NotEqual(result.RefreshToken, rawRow!.TokenHash);
    }

    // ── D3 验签 + exp + kid + 黑名单 ─────────────────────────────────

    [Fact]
    public async Task ValidateToken_ValidToken_Passes()
    {
        var (service, stub) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));
        var validation = await service.ValidateTokenAsync(result.AccessToken);

        Assert.Equal(account.UId, validation.UserId);
        Assert.Equal(AuthTypes.Sms, validation.AuthType);
        Assert.Equal((int)AuthLevel.Phone, validation.AuthLevel);
        // V0.9.0（A.4）：teacher_verified 属性移除——令牌不再携带业务声明
        Assert.True(validation.Claims.ContainsKey("iss"));
    }

    [Fact]
    public async Task ValidateToken_TamperedSignature_Rejected()
    {
        var (service, stub) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));
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
        var (service, stub) = CreateService(options);
        var account = CreateAccount();
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));
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
        var (service, stub) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));
        var validation = await service.ValidateTokenAsync(result.AccessToken);

        await service.RevokeTokenAsync(validation.Jti, "logout");
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ValidateTokenAsync(result.AccessToken));
        Assert.Equal("TOKEN_REVOKED", ex.Message);
    }

    // ── D4 Refresh rotation + TokenVersion + 重用检测 ────────────────

    [Fact]
    public async Task RefreshToken_Rotation_OldRevoked_NewPairIssued()
    {
        var (service, stub) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        var accountDs = stub.Use<AuthAccountEntityDataService>();
        await accountDs.CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));
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
        var (service, stub) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        var accountDs = stub.Use<AuthAccountEntityDataService>();
        await accountDs.CreateAsync(account);

        var result = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));

        // 密码/绑定变更 → TokenVersion++（闭环）
        await accountDs.IncrementTokenVersionAsync(account.UId);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.RefreshTokenAsync(result.RefreshToken));
        Assert.Equal("REFRESH_STALE", ex.Message);
    }

    [Fact]
    public async Task RefreshToken_ReuseDetected_RevokesAllForUser()
    {
        var (service, stub) = CreateService(AuthenticationTestHost.CreateOptions());
        var account = CreateAccount();
        var accountDs = stub.Use<AuthAccountEntityDataService>();
        await accountDs.CreateAsync(account);

        // 签发两个 refresh（同账号）
        var r1 = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));
        var r2 = await service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone));

        // 正常刷新 r2（旧 r2 变 revoked）
        await service.RefreshTokenAsync(r2.RefreshToken);
        // 重用旧 r2 → 泄露 → 撤销该用户全部 refresh（含 r1）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.RefreshTokenAsync(r2.RefreshToken));
        Assert.Equal("REFRESH_REUSED", ex.Message);

        var refreshDs = stub.Use<AuthRefreshTokenEntityDataService>();
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
        var (service, stub) = CreateService(options);
        var account = CreateAccount();
        stub.Use<AuthAccountEntityDataService>().CreateAsync(account).GetAwaiter().GetResult();

        var result = service.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone))
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
