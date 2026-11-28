using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace TKWF.Federation.QQ.Tests;

/// <summary>
/// QQ 畸形响应typed 异常回归网（代码审核 2026-11-23 P1 项 6）：
/// 976c5df 将 <c>root.GetProperty("...").GetString()!</c>（缺键抛底层异常、易被编排层错标
/// CHANNEL_REGISTRY_UNAVAILABLE）改为显式 <see cref="InvalidOperationException"/> + 机器可读消息。
/// </summary>
public class QqMalformedResponseTests
{
    private const string AppId = "test-appid";
    private const string RedirectUri = "https://test.local/sso/oauth/qq/callback";

    private static QqApiClient CreateClient(string tokenJson, string meJson)
    {
        var options = Options.Create(new QqOptions
        {
            Channels = [new QqChannelConfig
            {
                ChannelId = "qq-main",
                AppId = AppId,
                AppSecret = "test-secret"
            }]
        });
        var handler = new StubHandler(tokenJson, meJson);
        return new QqApiClient(new HttpClient(handler), options, NullLogger<QqApiClient>.Instance);
    }

    [Fact]
    public async Task GetAccessTokenAsync_MissingAccessToken_ThrowsTypedException()
    {
        // access_token 缺键（响应合法 JSON 但无该字段）→ 显式 InvalidOperationException，非底层 KeyNotFound
        var client = CreateClient("{\"expires_in\":7776000}", "{}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAccessTokenAsync(AppId, "code-1", RedirectUri, CancellationToken.None));

        Assert.Contains("缺少 access_token", ex.Message);
    }

    [Fact]
    public async Task GetMeAsync_MissingOpenId_ThrowsTypedException()
    {
        // openid 缺键 → 显式 InvalidOperationException（防编排层错标通道不可用）
        var client = CreateClient("{\"access_token\":\"token\"}", "{\"client_id\":\"test-appid\"}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetMeAsync(AppId, "token", CancellationToken.None));

        Assert.Contains("缺少 openid", ex.Message);
    }

    /// <summary>按路径返回指定 JSON 的桩 handler（200 + application/json）。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _tokenJson;
        private readonly string _meJson;

        public StubHandler(string tokenJson, string meJson)
        {
            _tokenJson = tokenJson;
            _meJson = meJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            var json = path.EndsWith("/oauth2.0/token", StringComparison.Ordinal) ? _tokenJson
                : path.EndsWith("/oauth2.0/me", StringComparison.Ordinal) ? _meJson
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
