using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;
using Xunit;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// <see cref="WeChatOauthChannel.BuildAuthorizeUrlAsync"/> 授权 URL 构造测试（子应用消费方接入 2026-10-11——
/// <see cref="ISsoChannel.BuildAuthorizeUrlAsync"/> DIM 契约化落地，ADR-Federation-子应用委托授权URL构造契约化）。
/// <para>真实 ChannelConfig（微信公共列 AppId）→ 授权 URL 结构断言（appid + redirect_uri + response_type=code +
/// scope 默认 snsapi_base + state + <c>#wechat_redirect</c> 尾缀）；模板实例（channel=null）→ 返回 null（DIM null）。
/// 直构通道（StubDomainUser——BuildAuthorizeUrlAsync 不经 API 出站）。</para>
/// </summary>
public class WeChatOauthChannelBuildAuthorizeUrlTests
{
    private const string TestAppId = "wx0000000000000000";
    private const string TestRedirectUri = "https://sso.example.com/sso/oauth/mp-main/callback";
    private const string TestState = "st-ticket-1";

    private static WeChatOauthChannel CreateChannel(ChannelConfig? channel)
        => new(new StubDomainUser(),
            new WeChatApiClient(new System.Net.Http.HttpClient(), Options.Create(new WeChatOptions()), NullLogger<WeChatApiClient>.Instance),
            channel);

    [Fact]
    public async Task BuildAuthorizeUrl_RealConfig_AppIdDefaultScope_WechatRedirect()
    {
        var channel = CreateChannel(new ChannelConfig
        {
            ChannelId = "mp-main",
            PlatformType = "wechat",
            AppId = TestAppId,
            IsEnabled = true,
        });

        var result = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext(TestRedirectUri, TestState, null, new Dictionary<string, string?>()),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("https://open.weixin.qq.com/connect/oauth2/authorize?", result!.Url);
        Assert.Contains($"appid={TestAppId}", result.Url);                       // AppId 公共列
        Assert.Contains($"redirect_uri={System.Uri.EscapeDataString(TestRedirectUri)}", result.Url);
        Assert.Contains("response_type=code", result.Url);
        Assert.Contains("scope=snsapi_base", result.Url);                        // 默认 scope（静默授权）
        Assert.Contains($"state={TestState}", result.Url);
        Assert.EndsWith("#wechat_redirect", result.Url);                         // 公众号网页授权尾缀
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
