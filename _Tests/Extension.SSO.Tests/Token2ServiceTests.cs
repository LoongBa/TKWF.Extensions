using System;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.SSO.Tests;

/// <summary>
/// token2 服务测试（T2——ES256 手写 JWT + 独立密钥域 + JWKS）。
/// <para>对齐 TokenServiceTests 模式：分层单测直构门面（StubDomainUser），生产路径集成见 <see cref="SsoProductionPathTests"/>。</para>
/// </summary>
public class Token2ServiceTests
{
    private static Token2Service CreateService()
    {
        var fsql = SsoTestHost.CreateInMemoryFreeSql();
        var stub = SsoTestHost.CreateStub(fsql);
        var options = SsoTestHost.CreateOptions();
        var service = new Token2Service(stub, Options.Create(options), NullLogger<Token2Service>.Instance);
        return service;
    }

    [Fact]
    public async Task Issue_And_Validate_RoundTrip()
    {
        var service = CreateService();
        var issued = await service.IssueToken2Async(new Token2IssueRequest("u-100", "app-1", "profile:basic"), default);

        var validated = await service.ValidateToken2Async(issued.Token, default);
        Assert.Equal("u-100", validated.UId);
        Assert.Equal("app-1", validated.TargetAppId);
        Assert.NotNull(validated.Jti);
        Assert.True(validated.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Validate_RejectsTamperedToken()
    {
        var service = CreateService();
        var issued = await service.IssueToken2Async(new Token2IssueRequest("u-100", "app-1", null), default);

        // 篡改 payload（sub 改 u-999）→ 签名失效
        var parts = issued.Token.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Token2Service.Base64UrlDecode(parts[1]))
            .Replace("u-100", "u-999");
        var forged = parts[0] + "." + Token2Service.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(payload)) + "." + parts[2];

        await Assert.ThrowsAsync<AuthenticationException>(() => service.ValidateToken2Async(forged, default));
    }

    [Fact]
    public async Task Validate_RejectsUnknownKid()
    {
        var service = CreateService();
        var issued = await service.IssueToken2Async(new Token2IssueRequest("u-100", "app-1", null), default);

        // 直接构造 kid=unknown 的 header（签名部分原样——kid 白名单先于签名校验拒绝）
        var forgedHeader = "{\"alg\":\"ES256\",\"typ\":\"JWT\",\"kid\":\"unknown-kid\"}";
        var parts = issued.Token.Split('.');
        var forged = SignatureHeader(parts[1]);

        await Assert.ThrowsAsync<AuthenticationException>(() => service.ValidateToken2Async(forged, default));
    }

    private static string SignatureHeader(string payloadB64)
    {
        var forgedHeader = "{\"alg\":\"ES256\",\"typ\":\"JWT\",\"kid\":\"unknown-kid\"}";
        return Token2Service.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(forgedHeader)) + "." + payloadB64 + ".fake";
    }

    [Fact]
    public async Task Validate_RejectsExpiredToken()
    {
        var fsql = SsoTestHost.CreateInMemoryFreeSql();
        var stub = SsoTestHost.CreateStub(fsql);
        var options = SsoTestHost.CreateOptions();
        var service = new Token2Service(stub, Options.Create(options), NullLogger<Token2Service>.Instance);

        // 直接构造已过期 token：ttl=0 签发（exp=now）→ 验签 service 用同密钥但 ClockSkew=0（无 leeway 覆盖）
        var shortOptions = new SsoOptions
        {
            Issuer = options.Issuer, SigningKeyPath = options.SigningKeyPath, CurrentKid = options.CurrentKid,
            SigningKeys = options.SigningKeys, Token2ExpirationSeconds = 0, ClockSkewSeconds = 0, IsProduction = true,
        };
        var shortService = new Token2Service(stub, Options.Create(shortOptions), NullLogger<Token2Service>.Instance);
        var issued = await shortService.IssueToken2Async(new Token2IssueRequest("u-100", "app-1", null), default);
        // 同密钥验签（shortOptions 与 options 同 SigningKeys——签发/验签密钥一致），但 exp=now 且 leeway=0 → TOKEN_EXPIRED
        var verifyOptions = new SsoOptions
        {
            Issuer = options.Issuer, SigningKeyPath = options.SigningKeyPath, CurrentKid = options.CurrentKid,
            SigningKeys = options.SigningKeys, Token2ExpirationSeconds = 300, ClockSkewSeconds = 0, IsProduction = true,
        };
        var verifyService = new Token2Service(stub, Options.Create(verifyOptions), NullLogger<Token2Service>.Instance);
        await Assert.ThrowsAsync<AuthenticationException>(() => verifyService.ValidateToken2Async(issued.Token, default));
    }

    [Fact]
    public async Task Jwks_ContainsCurrentKid()
    {
        var service = CreateService();
        var jwks = service.GetJwksJson();
        Assert.Contains("sso-test-key-1", jwks);
        Assert.Contains("ES256", jwks);
    }

    [Fact]
    public async Task MultiKid_Rotation_OldKidStillVerifies()
    {
        var fsql = SsoTestHost.CreateInMemoryFreeSql();
        var stub = SsoTestHost.CreateStub(fsql);
        var (key1Private, key1Public) = SsoTestHost.CreateEcKeyFiles("sso-old-key");
        var (key2Private, key2Public) = SsoTestHost.CreateEcKeyFiles("sso-new-key");

        // 轮换期：SigningKeys 含两 kid，CurrentKid = 新
        var options = new SsoOptions
        {
            Issuer = "https://sso.test.local",
            SigningKeyPath = key2Private,
            CurrentKid = "sso-new-key",
            SigningKeys =
            [
                new SsoSigningKeyConfig { Kid = "sso-old-key", PrivateKeyPath = key1Private, PublicKeyPath = key1Public },
                new SsoSigningKeyConfig { Kid = "sso-new-key", PrivateKeyPath = key2Private, PublicKeyPath = key2Public },
            ],
            IsProduction = true,
        };
        var service = new Token2Service(stub, Options.Create(options), NullLogger<Token2Service>.Instance);

        // 旧密钥签发（构造旧 service）→ 新 service 仍可验签（verifyKeys 遍历含旧 kid）
        var oldOptions = new SsoOptions
        {
            Issuer = options.Issuer, SigningKeyPath = key1Private, CurrentKid = "sso-old-key", SigningKeys = options.SigningKeys, IsProduction = true,
        };
        var oldService = new Token2Service(stub, Options.Create(oldOptions), NullLogger<Token2Service>.Instance);
        var issued = await oldService.IssueToken2Async(new Token2IssueRequest("u-100", "app-1", null), default);

        var validated = await service.ValidateToken2Async(issued.Token, default);   // 新 service 验旧签名
        Assert.Equal("u-100", validated.UId);
    }
}
