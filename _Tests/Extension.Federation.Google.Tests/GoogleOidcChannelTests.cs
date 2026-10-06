using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Federation.Google.Tests;

/// <summary>
/// Google 平台网关测试（M2——public sub 通配 `google_oidc:*`，OIDC 参考实现基线）：
/// 生产路径宿主（真实 DI + AddGoogleFederationChannels + 探针门面帧内枚举）+ Stub Google IdP（本机 RSA 签发）。
/// 覆盖验收：F1（纯库判定）/ F2（OAuth 正负 + id_token 篡改拒）/ F3（public sub 通配断言）/ F4（注册进集合）。
/// </summary>
public class GoogleOidcChannelTests : GoogleChannelTestHost
{
    private const string GoogleIssuer = "https://accounts.google.com";
    private const string TestSub = "google_stub_sub";
    private const string Kid = "google-kid";

    private RSA _privateKey = RSA.Create(2048);
    private StubGoogleHandler.StubOptions _stub = new();

    private (DomainUser<TestUserInfo> User, GoogleOidcChannel Channel) Setup(Func<StubGoogleHandler.StubOptions, StubGoogleHandler.StubOptions>? mutate = null)
    {
        if (mutate != null) _stub = mutate(_stub);
        Handler = new StubGoogleHandler(_privateKey, Kid, () => _stub);
        var (_, user) = BindScope();
        var probe = user.Use<IGoogleChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "google_oidc");
        return (user, (GoogleOidcChannel)channel);
    }

    // ---- F1：纯库判定 ----

    [Fact]
    public void Registration_ChannelType_GoogleOidc()
    {
        ConfigureOptions = o => o.Channels = [new GoogleChannelConfig
        {
            ChannelId = "google-main", ClientId = "google-client-1", ClientSecret = "google-secret",
            TokenIssuers = [GoogleIssuer],
        }];
        var (_, user) = BindScope();
        var probe = user.Use<IGoogleChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "google_oidc");
        Assert.Equal("google_oidc", channel.ChannelType);
    }

    // ---- F3：public sub 通配 channel_id（N2 §3.3）----

    [Fact]
    public void ChannelId_PublicWildcard_Is_GoogleOidc_Star()
    {
        var (_, channel) = Setup();
        Assert.Equal("google_oidc:*", channel.ChannelId);   // public 平台 client_id 通配（N2 §3.3 定案）
    }

    // ---- F2：OAuth 正负 + id_token 验签 ----

    [Fact]
    public async Task AuthenticateAsync_ValidCode_ReturnsSub_Success()
    {
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(GoogleTestHost.CreateContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestSub, result.ExternalUserId);   // sub = 外部身份键
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingCode_Fails()
    {
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(GoogleTestHost.CreateContext(withCode: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OIDC_CODE_REQUIRED", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_TamperedIdToken_Rejected()
    {
        var (_, channel) = Setup(s => s with { Tampered = true });

        var result = await channel.AuthenticateAsync(GoogleTestHost.CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.ExternalUserId);
        Assert.NotEmpty(result.FailReason);   // 签名验签失败（信任根负路径）
    }

    [Fact]
    public async Task AuthenticateAsync_AlgMismatch_Rejected()
    {
        var (_, channel) = Setup(s => s with { AlgOverride = "HS256" });

        var result = await channel.AuthenticateAsync(GoogleTestHost.CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("alg", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ExpiredIdToken_Rejected()
    {
        var (_, channel) = Setup(s => s with { ExpOverrideSecs = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() });

        var result = await channel.AuthenticateAsync(GoogleTestHost.CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("过期", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingVerifier_PkceEnabled_Rejected()
    {
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(GoogleTestHost.CreateContext(withVerifier: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("code_verifier", result.FailReason);   // PKCE S256 缺失拒
    }

    // ---- F4：注册进集合 + 未装配自然跳过（Oidc 基座语义）----

    [Fact]
    public void Registration_ProbeEnumerates_GoogleChannel()
    {
        ConfigureOptions = o => o.Channels = [new GoogleChannelConfig
        {
            ChannelId = "google-main", ClientId = "google-client-1", ClientSecret = "google-secret",
            TokenIssuers = [GoogleIssuer],
        }];
        var (_, user) = BindScope();
        var probe = user.Use<IGoogleChannelProbe>();
        Assert.Contains(probe.Channels, c => c.ChannelType == "google_oidc");
    }
}