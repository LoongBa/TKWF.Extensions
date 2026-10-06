using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Federation.Oidc.Tests;

/// <summary>
/// 直配通道测试（<c>AddOidcFederationChannel</c>——自托管 IdP：Keycloak/Okta 形态）——
/// 生产路径宿主（真实 DI + 探针门面帧内枚举）+ Stub OIDC IdP（本机 RSA 签发 id_token + JWKS）。
/// <para>覆盖验收：F2（id_token JWKS 验签正负 + issuer 容错）、F3（sub 语义）、F4（Discovery 配置驱动）、
/// F6（PKCE）、F4（注册进集合——探针枚举）。</para>
/// </summary>
public class OidcConfiguredChannelTests : OidcChannelTestHost
{
    private const string Issuer = "https://stub.example.com";
    private const string ClientId = "stub-client";
    private const string TestSub = "oidc_stub_sub";
    private const string Kid = "test-kid";

    private RSA _privateKey = RSA.Create(2048);
    private StubOidcHandler.OidcStubOptions _stub = new(Issuer, ClientId, TestSub);

    private (DomainUser<TestUserInfo> User, ISsoChannel Channel) Setup(Func<StubOidcHandler.OidcStubOptions, StubOidcHandler.OidcStubOptions>? mutate = null)
    {
        if (mutate != null) _stub = mutate(_stub);
        TestOidcHandler = new StubOidcHandler(_privateKey, Kid, () => _stub);
        var (_, user) = BindScope();
        var probe = user.Use<IOidcChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "stub_oidc");
        return (user, channel);
    }

    // ---- F2：id_token JWKS 验签正负 ----

    [Fact]
    public void Registration_ChannelType_StubOidc_And_ChannelId_FromConfig()
    {
        ConfigureOptions = o => o.ChannelId = "keycloak-corp";
        var (_, user) = BindScope();
        var probe = user.Use<IOidcChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "stub_oidc");
        Assert.Equal("stub_oidc", channel.ChannelType);      // ChannelType = {Platform}_oidc
        Assert.Equal("keycloak-corp", channel.ChannelId);    // ChannelId = 配置直填（默认 BuildChannelId）
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCode_ReturnsSub_Success()
    {
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(CreateCode(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestSub, result.ExternalUserId);   // sub 不透明字符串 = 外部身份键（P12）
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);              // OIDC 便捷档（对齐微信 OAuth 语义）
    }

    [Fact]
    public async Task AuthenticateAsync_MissingCode_Fails()
    {
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(
            OidcTestHost.CreateContext(("state", "abc")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OIDC_CODE_REQUIRED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_TamperedIdToken_Rejected()
    {
        var (_, channel) = Setup(s => s with { Tampered = true });   // 篡改 sub → 验签失败

        var result = await channel.AuthenticateAsync(CreateCode(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.ExternalUserId);
        Assert.NotEmpty(result.FailReason);   // 签名验签失败（不静默放行）
    }

    [Fact]
    public async Task AuthenticateAsync_AlgMismatch_Rejected()
    {
        var (_, channel) = Setup(s => s with { AlgOverride = "HS256" });   // 算法混淆拒

        var result = await channel.AuthenticateAsync(CreateCode(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("id_token alg 非 RS256（实际 HS256——算法混淆拒）", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ExpiredIdToken_Rejected()
    {
        var (_, channel) = Setup(s => s with { ExpOverrideSecs = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() });

        var result = await channel.AuthenticateAsync(CreateCode(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotEmpty(result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_NbfInFuture_Rejected()
    {
        var (_, channel) = Setup(s => s with { NbfOverride = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds() });

        var result = await channel.AuthenticateAsync(CreateCode(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("nbf", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongAudience_Rejected()
    {
        var (_, channel) = Setup(s => s with { AudOverride = "other-app" });

        var result = await channel.AuthenticateAsync(CreateCode(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("aud", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_IssuerNotInWhitelist_Rejected()
    {
        var (_, channel) = Setup(s => s with { Issuer = "https://evil.example.com" });

        var result = await channel.AuthenticateAsync(CreateCode(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("iss", result.FailReason);   // TokenIssuer 白名单拒
    }

    // ---- F2 issuer 容错：通配/正则白名单（Microsoft common tenant iss 含 GUID）----

    [Fact]
    public void IsIssuerAllowed_RegexPattern_MatchesTenantGuid()
    {
        Assert.True(OidcIdTokenValidator.IsIssuerAllowed(
            "https://login.microsoftonline.com/1a2b3c4d-5e6f-7890-abcd-ef1234567890/v2.0",
            ["^https://login\\.microsoftonline\\.com/[^/]+/v2\\.0$"]));
    }

    [Fact]
    public void IsIssuerAllowed_WildcardSuffix_Matches()
    {
        Assert.True(OidcIdTokenValidator.IsIssuerAllowed(
            "https://login.microsoftonline.com/whatever/v2.0",
            ["https://login.microsoftonline.com/*"]));
    }

    [Fact]
    public void IsIssuerAllowed_ExactMatch_Matches_And_Mismatch_Rejects()
    {
        Assert.True(OidcIdTokenValidator.IsIssuerAllowed(Issuer, [Issuer]));
        Assert.False(OidcIdTokenValidator.IsIssuerAllowed("https://other.example.com", [Issuer]));
    }

    // ---- F4：Discovery 配置驱动（token/userinfo/jwks 经 Discovery 解析覆盖）----

    [Fact]
    public async Task AuthenticateAsync_DiscoveryResolver_TokenAndJwksFromDiscovery()
    {
        // 配置只给 DiscoveryUri（token/jwks 由 Discovery 端点解析）——链路正向
        ConfigureOptions = o =>
        {
            o.DiscoveryUri = "https://stub.example.com/.well-known/openid-configuration";
            o.TokenUri = "";   // 清空显式——走 Discovery
            o.JwksUri = "";
            o.AuthorizeUri = "";
        };
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(CreateCode(), CancellationToken.None);

        Assert.True(result.Success);   // Discovery 解析 token/jwks 成功 → 验签通过
    }

    // ---- F6：PKCE ----

    [Fact]
    public void Pkce_VerifierChallenge_RoundTrip()
    {
        var verifier = OidcAuthFlow.GenerateCodeVerifier();
        var challenge = OidcAuthFlow.ComputeCodeChallenge(verifier);
        Assert.NotEmpty(verifier);
        Assert.NotEqual(verifier, challenge);
        Assert.True(challenge.Length is > 20 and < 100);   // base64url(32) ≈ 43 字符
    }

    [Fact]
    public async Task AuthenticateAsync_PkceEnabled_MissingVerifier_Rejected()
    {
        // UsePkce 默认 true——context 无 code_verifier → 拒绝（OidcAuthFlow 校验）
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(CreateCode(includeVerifier: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("code_verifier", result.FailReason);
    }

    // ---- helper ----

    private static SsoChannelAuthContext CreateCode(bool includeVerifier = true)
        => OidcTestHost.CreateContext(
            ("code", "valid-code-1"),
            ("redirect_uri", "https://app.example.com/callback"),
            includeVerifier ? ("code_verifier", OidcAuthFlow.GenerateCodeVerifier()) : ("code_verifier", null));
}
