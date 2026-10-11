using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;
using Xunit;

namespace TKWF.Federation.QQ.Tests;

/// <summary>
/// <see cref="QqOauthChannel.BuildAuthorizeUrlAsync"/> 授权 URL 构造测试（子应用消费方接入 2026-10-11——
/// <see cref="ISsoChannel.BuildAuthorizeUrlAsync"/> DIM 契约化落地，ADR-Federation-子应用委托授权URL构造契约化）。
/// <para>真实 ChannelConfig（QQ 公共列 AppId——client_id）→ 授权 URL 结构断言（client_id + redirect_uri +
/// state + scope 默认 get_user_info）；模板实例（channel=null——集合类型索引源）→ 返回 null（DIM null 语义，
/// 端点映射 AUTHORIZE_NOT_SUPPORTED）。直构通道（StubDomainUser——BuildAuthorizeUrlAsync 不经 API 出站）。</para>
/// </summary>
public class QqOauthChannelBuildAuthorizeUrlTests
{
    private const string TestAppId = "101000000";
    private const string TestRedirectUri = "https://sso.example.com/sso/oauth/qq-main/callback";
    private const string TestState = "st-ticket-1";

    private static QqOauthChannel CreateChannel(ChannelConfig? channel)
        => new(new StubDomainUser(),
            new QqApiClient(new System.Net.Http.HttpClient(), Options.Create(new QqOptions()), NullLogger<QqApiClient>.Instance),
            channel);

    [Fact]
    public async Task BuildAuthorizeUrl_RealConfig_ClientIdStateScope()
    {
        var channel = CreateChannel(new ChannelConfig
        {
            ChannelId = "qq-main",
            PlatformType = "qq",
            AppId = TestAppId,
            IsEnabled = true,
        });

        var result = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext(TestRedirectUri, TestState, null, new Dictionary<string, string?>()),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("https://graph.qq.com/oauth2.0/authorize?", result!.Url);
        Assert.Contains($"client_id={TestAppId}", result.Url);                 // AppId 公共列 → client_id
        Assert.Contains($"redirect_uri={System.Uri.EscapeDataString(TestRedirectUri)}", result.Url);
        Assert.Contains($"state={TestState}", result.Url);                     // state 必填（Platform 签名票据）
        Assert.Contains("scope=get_user_info", result.Url);                    // 默认 scope（QQ 网页授权）
        Assert.Equal(TestState, result.State);                                 // state 回传
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
