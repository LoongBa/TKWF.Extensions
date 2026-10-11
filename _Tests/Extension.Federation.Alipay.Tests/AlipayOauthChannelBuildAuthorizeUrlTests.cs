using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;
using Xunit;

namespace TKWF.Federation.Alipay.Tests;

/// <summary>
/// <see cref="AlipayOauthChannel.BuildAuthorizeUrlAsync"/> 授权 URL 构造测试（子应用消费方接入 2026-10-11——
/// <see cref="ISsoChannel.BuildAuthorizeUrlAsync"/> DIM 契约化落地，ADR-Federation-子应用委托授权URL构造契约化）。
/// <para>真实 ChannelConfig（支付宝公共列 AppId）→ 授权 URL 结构断言（app_id + scope 默认 auth_base +
/// redirect_uri + state——复用 <see cref="AlipayApiClient.BuildAuthorizeUrl"/>）；模板实例（channel=null）→
/// 返回 null（DIM null）。直构通道（StubDomainUser——BuildAuthorizeUrlAsync 不经 API 出站/签名）。</para>
/// </summary>
public class AlipayOauthChannelBuildAuthorizeUrlTests
{
    private const string TestAppId = "2021000000000000";
    private const string TestRedirectUri = "https://sso.example.com/sso/oauth/alipay-main/callback";
    private const string TestState = "st-ticket-1";

    private static AlipayOauthChannel CreateChannel(ChannelConfig? channel)
        => new(new StubDomainUser(),
            new AlipayApiClient(new System.Net.Http.HttpClient(),
                Options.Create(new AlipayOptions()), new AlipaySignService(), NullLogger<AlipayApiClient>.Instance),
            new AlipaySignService(),
            channel);

    [Fact]
    public async Task BuildAuthorizeUrl_RealConfig_AppIdDefaultScope_State()
    {
        var channel = CreateChannel(new ChannelConfig
        {
            ChannelId = "alipay-main",
            PlatformType = "alipay",
            AppId = TestAppId,
            IsEnabled = true,
        });

        var result = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext(TestRedirectUri, TestState, null, new Dictionary<string, string?>()),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("https://openauth.alipay.com/oauth2/publicAppAuthorize.htm?", result!.Url);
        Assert.Contains($"app_id={TestAppId}", result.Url);                        // AppId 公共列
        Assert.Contains("scope=auth_base", result.Url);                             // 默认 scope（静默仅 user_id）
        Assert.Contains($"redirect_uri={System.Uri.EscapeDataString(TestRedirectUri)}", result.Url);
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
