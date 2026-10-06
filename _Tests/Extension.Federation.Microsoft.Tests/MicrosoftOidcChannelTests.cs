using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Microsoft.Tests;

/// <summary>
/// Microsoft 平台网关测试（M2——pairwise sub 复合编码 + tenant 配置化）：
/// 生产路径宿主（真实 DI + AddMicrosoftFederationChannels + 探针门面帧内枚举）+ Stub Microsoft IdP。
/// 覆盖验收：F1（纯库判定）/ F2（OAuth 正负 + id_token 篡改拒）/ F3（**pairwise 复合编码断言**）/
/// M2-P1-1（**tenant iss 含实际 GUID——正则白名单匹配**）/ F4（注册进集合）。
/// </summary>
public class MicrosoftOidcChannelTests : MicrosoftChannelTestHost
{
    private const string Kid = "ms-kid";
    private const string TestSub = "ms_stub_sub";
    private const string TenantGuid = "guid-11111111-2222-3333-4444-555555555555";

    private RSA _privateKey = RSA.Create(2048);
    private StubMicrosoftHandler.StubOptions _stub = new();

    private (DomainUser<TestUserInfo> User, MicrosoftOidcChannel Channel) Setup(Func<StubMicrosoftHandler.StubOptions, StubMicrosoftHandler.StubOptions>? mutate = null)
    {
        if (mutate != null) _stub = mutate(_stub);
        Handler = new StubMicrosoftHandler(_privateKey, Kid, () => _stub);
        var (_, user) = BindScope();
        var probe = user.Use<IMicrosoftChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "microsoft_oidc");
        return (user, (MicrosoftOidcChannel)channel);
    }

    // ---- F3：pairwise 复合编码断言（N2 §3.3：microsoft_oidc:{client_id}——Oracle P1-2 自动拼接）----

    [Fact]
    public void ChannelId_PairwiseComposite_ContainsClientId()
    {
        ConfigureOptions = o => o.Channels = [new MicrosoftChannelConfig
        {
            ChannelId = "ms-main", ClientId = "microsoft-app-1", ClientSecret = "ms-secret", Tenant = "organizations",
            TokenIssuers = ["^https://login\\.microsoftonline\\.com/[^/]+/v2\\.0$"],
        }];
        var (_, user) = BindScope();
        var probe = user.Use<IMicrosoftChannelProbe>();
        var channel = (MicrosoftOidcChannel)Assert.Single(probe.Channels, c => c.ChannelType == "microsoft_oidc");

        Assert.Equal("microsoft_oidc:microsoft-app-1", channel.ChannelId);   // pairwise 复合编码（自动拼接）
    }

    [Fact]
    public void SameUser_DifferentClientId_DifferentChannelId_NoMerge()
    {
        // 同用户不同 App（pairwise 跨 App sub 不同 → 不同 channel_id）——N2 §3.2 不误并
        ConfigureOptions = o => o.Channels = [new MicrosoftChannelConfig
        {
            ChannelId = "app-a", ClientId = "app-a", ClientSecret = "s", Tenant = "organizations",
            TokenIssuers = ["^https://login\\.microsoftonline\\.com/[^/]+/v2\\.0$"],
        }];
        var (_, userA) = BindScope();
        var probeA = userA.Use<IMicrosoftChannelProbe>();
        var channelA = (MicrosoftOidcChannel)Assert.Single(probeA.Channels, c => c.ChannelType == "microsoft_oidc");
        Assert.Equal("microsoft_oidc:app-a", channelA.ChannelId);

        ConfigureOptions = o => o.Channels = [new MicrosoftChannelConfig
        {
            ChannelId = "app-b", ClientId = "app-b", ClientSecret = "s", Tenant = "organizations",
            TokenIssuers = ["^https://login\\.microsoftonline\\.com/[^/]+/v2\\.0$"],
        }];
        var (_, userB) = BindScope();
        var probeB = userB.Use<IMicrosoftChannelProbe>();
        var channelB = (MicrosoftOidcChannel)Assert.Single(probeB.Channels, c => c.ChannelType == "microsoft_oidc");
        Assert.Equal("microsoft_oidc:app-b", channelB.ChannelId);

        // 不同 channel_id → 映射表 (channel_id, sub) 不同行 → 不误并
        Assert.NotEqual(channelA.ChannelId, channelB.ChannelId);
    }

    // ---- M2-P1-1：tenant iss 含实际 GUID——正则白名单匹配 ----

    [Fact]
    public async Task AuthenticateAsync_CommonTenant_IssContainsGuid_MatchesRegexWhitelist()
    {
        // common tenant 授权后 iss = https://login.microsoftonline.com/{guid}/v2.0（非字面 common）
        // 默认 Channels 用正则白名单（^https://login\.microsoftonline\.com/[^/]+/v2\.0$）——应匹配
        var (_, channel) = Setup(s => s with { Tenant = "common", TenantGuid = TenantGuid });

        var result = await channel.AuthenticateAsync(MicrosoftTestHost.CreateContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestSub, result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_EvilIssuer_RejectedByRegexWhitelist()
    {
        // 攻击者签发 iss=evil 域（IssuerOverride）——正则白名单拒
        var (_, channel) = Setup(s => s with { IssuerOverride = "https://evil.example.com/v2.0" });

        var result = await channel.AuthenticateAsync(MicrosoftTestHost.CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("iss", result.FailReason);
    }

    // ---- F2：OAuth 正负 + id_token 验签 ----

    [Fact]
    public async Task AuthenticateAsync_ValidCode_ReturnsSub_Success()
    {
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(MicrosoftTestHost.CreateContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestSub, result.ExternalUserId);
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingCode_Fails()
    {
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(MicrosoftTestHost.CreateContext(withCode: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OIDC_CODE_REQUIRED", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_TamperedIdToken_Rejected()
    {
        var (_, channel) = Setup(s => s with { Tampered = true });

        var result = await channel.AuthenticateAsync(MicrosoftTestHost.CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.ExternalUserId);
        Assert.NotEmpty(result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_AlgMismatch_Rejected()
    {
        var (_, channel) = Setup(s => s with { AlgOverride = "HS256" });

        var result = await channel.AuthenticateAsync(MicrosoftTestHost.CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("alg", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ExpiredIdToken_Rejected()
    {
        var (_, channel) = Setup(s => s with { ExpOverrideSecs = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds() });

        var result = await channel.AuthenticateAsync(MicrosoftTestHost.CreateContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("过期", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingVerifier_PkceEnabled_Rejected()
    {
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(MicrosoftTestHost.CreateContext(withVerifier: false), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("code_verifier", result.FailReason);
    }

    // ---- F4：注册进集合 ----

    [Fact]
    public void Registration_ProbeEnumerates_MicrosoftChannel()
    {
        var (_, user) = BindScope();
        var probe = user.Use<IMicrosoftChannelProbe>();
        Assert.Contains(probe.Channels, c => c.ChannelType == "microsoft_oidc");
    }
}