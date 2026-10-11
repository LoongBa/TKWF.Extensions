using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;
using Xunit;

namespace TKWF.Federation.WeCom.Tests;

/// <summary>
/// <see cref="WeComOauthChannel.BuildAuthorizeUrlAsync"/> 授权 URL 构造测试（子应用消费方接入 2026-10-11——
/// <see cref="ISsoChannel.BuildAuthorizeUrlAsync"/> DIM 契约化落地，ADR-Federation-子应用委托授权URL构造契约化）。
/// <para>真实 ChannelConfig（企业微信 CorpId 进 <see cref="ChannelConfig.Extra"/>——M7 法人级标识不挤占 AppId）：
/// 双授权流——<b>webview 默认流</b>（<c>open.weixin.qq.com/connect/oauth2/authorize</c> + DefaultScope 兜底 +
/// <c>#wechat_redirect</c>）/ <b>扫码流</b>（<c>login.work.weixin.qq.com/wwlogin/sso/login</c>——Parameters["flow"]="scan"，
/// login_type=CorpApp）；模板实例（channel=null）→ 返回 null（DIM null）。直构通道（StubDomainUser——
/// BuildAuthorizeUrlAsync 不经 API 出站）。</para>
/// </summary>
public class WeComOauthChannelBuildAuthorizeUrlTests
{
    private const string TestCorpId = "ww_test_corp";
    private const string TestAgentId = "1000002";
    private const string TestRedirectUri = "https://sso.example.com/sso/oauth/wecom-main/callback";
    private const string TestState = "st-ticket-1";

    private static WeComOauthChannel CreateChannel(ChannelConfig? channel)
        => new(new StubDomainUser(),
            new WeComApiClient(new System.Net.Http.HttpClient(), NullLogger<WeComApiClient>.Instance),
            channel);

    private static ChannelConfig RealConfig(string? defaultScope = null)
        => new()
        {
            ChannelId = "wecom-main",
            PlatformType = "wecom",
            IsEnabled = true,
            Extra = new Dictionary<string, string?>
            {
                ["CorpId"] = TestCorpId,
                ["AgentId"] = TestAgentId,
                ["IsThirdParty"] = "false",
                ["DefaultScope"] = defaultScope,       // 可空——兜底 snsapi_base
            },
        };

    /// <summary>webview 默认流（无 flow 参数）：open.weixin.qq.com/connect/oauth2/authorize + DefaultScope 兜底 + #wechat_redirect。</summary>
    [Fact]
    public async Task BuildAuthorizeUrl_RealConfig_WebviewDefaultFlow_DefaultScopeFallback()
    {
        var channel = CreateChannel(RealConfig(defaultScope: null));   // DefaultScope 空 → 兜底 snsapi_base

        var result = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext(TestRedirectUri, TestState, null, new Dictionary<string, string?>()),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("https://open.weixin.qq.com/connect/oauth2/authorize?", result!.Url);
        Assert.Contains($"appid={TestCorpId}", result.Url);            // CorpId Extra → appid
        Assert.Contains($"redirect_uri={System.Uri.EscapeDataString(TestRedirectUri)}", result.Url);
        Assert.Contains("response_type=code", result.Url);
        Assert.Contains("scope=snsapi_base", result.Url);              // DefaultScope 空 → snsapi_base 兜底
        Assert.Contains($"state={TestState}", result.Url);
        Assert.EndsWith("#wechat_redirect", result.Url);               // Webview 流尾缀
        Assert.Equal(TestState, result.State);
    }

    /// <summary>webview 默认流——配置 DefaultScope（Extra 兜底）生效（snsapi_privateinfo）。</summary>
    [Fact]
    public async Task BuildAuthorizeUrl_RealConfig_Webview_ConfiguredDefaultScope()
    {
        var channel = CreateChannel(RealConfig(defaultScope: "snsapi_privateinfo"));

        var result = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext(TestRedirectUri, TestState, null, new Dictionary<string, string?>()),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("scope=snsapi_privateinfo", result!.Url);      // DefaultScope 兜底（Extra）生效
        Assert.EndsWith("#wechat_redirect", result.Url);
    }

    /// <summary>扫码流（Parameters["flow"]="scan"）：login.work.weixin.qq.com/wwlogin/sso/login + login_type=CorpApp（自建）。</summary>
    [Fact]
    public async Task BuildAuthorizeUrl_RealConfig_ScanFlow_LoginWorkWeixin()
    {
        var channel = CreateChannel(RealConfig());

        var result = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext(TestRedirectUri, TestState, null,
                new Dictionary<string, string?> { ["flow"] = "scan" }),   // 扫码流选择器
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.StartsWith("https://login.work.weixin.qq.com/wwlogin/sso/login?", result!.Url);
        Assert.Contains("login_type=CorpApp", result.Url);               // 自建应用（IsThirdParty=false）
        Assert.Contains($"appid={TestCorpId}", result.Url);
        Assert.Contains($"agentid={TestAgentId}", result.Url);
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
