using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;
using Xunit;

namespace TKWF.Federation.DingTalk.Tests;

/// <summary>
/// <see cref="DingTalkOauthChannel.BuildAuthorizeUrlAsync"/> 授权 URL 构造测试（子应用消费方接入 2026-10-11——
/// <see cref="ISsoChannel.BuildAuthorizeUrlAsync"/> DIM 契约化落地，ADR-Federation-子应用委托授权URL构造契约化）。
/// <para>真实 ChannelConfig（钉钉 AppKey 进 <see cref="ChannelConfig.Extra"/>——M7 映射约束，非公共列 AppId）→
/// 授权 URL 结构断言（client_id=AppKey + redirect_uri + response_type=code + scope 默认 openid +
/// prompt=consent + state）；模板实例（channel=null）→ 返回 null（DIM null）。直构通道（StubDomainUser——
/// BuildAuthorizeUrlAsync 不经 API 出站）。</para>
/// </summary>
public class DingTalkOauthChannelBuildAuthorizeUrlTests
{
    private const string TestAppKey = "dingAppKey000000000000";
    private const string TestRedirectUri = "https://sso.example.com/sso/oauth/dingtalk-main/callback";
    private const string TestState = "st-ticket-1";

    private static DingTalkOauthChannel CreateChannel(ChannelConfig? channel)
        => new(new StubDomainUser(),
            new DingTalkApiClient(new System.Net.Http.HttpClient(), Options.Create(new DingTalkOptions()), NullLogger<DingTalkApiClient>.Instance),
            channel);

    [Fact]
    public async Task BuildAuthorizeUrl_RealConfig_AppKeyExtra_ScopeOpenId_PromptConsent()
    {
        var channel = CreateChannel(new ChannelConfig
        {
            ChannelId = "dingtalk-main",
            PlatformType = "dingtalk",
            IsEnabled = true,
            Extra = new Dictionary<string, string?> { ["AppKey"] = TestAppKey },   // M7：AppKey 进 Extra
        });

        var result = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext(TestRedirectUri, TestState, null, new Dictionary<string, string?>()),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("https://login.dingtalk.com/oauth2/auth?", result!.Url);
        Assert.Contains($"client_id={TestAppKey}", result.Url);                   // AppKey Extra → client_id
        Assert.Contains($"redirect_uri={System.Uri.EscapeDataString(TestRedirectUri)}", result.Url);
        Assert.Contains("response_type=code", result.Url);
        Assert.Contains("scope=openid", result.Url);                               // 默认 scope
        Assert.Contains("prompt=consent", result.Url);                             // prompt=consent 必填
        Assert.Contains($"state={TestState}", result.Url);
        Assert.Equal(TestState, result.State);
    }

    [Fact]
    public async Task BuildAuthorizeUrl_TemplateInstance_ReturnsNull()
    {
        // 模板实例（channel=null——集合类型索引源）——BuildAuthorizeUrlAsync DIM null → AUTHORIZE_NOT_SUPPORTED
        var channel = CreateChannel(null);

        var result = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext(TestRedirectUri, TestState, null, new Dictionary<string, string?>()),
            CancellationToken.None);

        Assert.Null(result);
    }
}
