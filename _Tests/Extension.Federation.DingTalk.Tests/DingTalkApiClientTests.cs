using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKWF.Federation.DingTalk;
using Xunit;

namespace TKWF.Federation.DingTalk.Tests;

/// <summary>
/// <see cref="DingTalkApiClient"/> 出站客户端直构测试（new HttpClient(stub) + Options + NullLogger）——
/// DT-4：HTTP 非 2xx 状态 → 与同文件其他方法一致的 IsSuccessStatusCode 错误路径（InvalidOperationException）。
/// </summary>
public class DingTalkApiClientTests
{
    private const string TestAppKey = "dingAppKey000000000000";

    /// <summary>HTTP 错误状态码桩——errorPathSuffix 路径返回 500，gettoken 正常（200）供 getbyunionid 链路前置。</summary>
    private sealed class HttpErrorHandler : HttpMessageHandler
    {
        private readonly string _errorPathSuffix;

        public HttpErrorHandler(string errorPathSuffix) => _errorPathSuffix = errorPathSuffix;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith(_errorPathSuffix, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"errcode\":-1,\"errmsg\":\"server error\"}", Encoding.UTF8, "application/json"),
                });
            if (path.EndsWith("/gettoken", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"errcode\":0,\"access_token\":\"stub-legacy-token\",\"expires_in\":7200}", Encoding.UTF8, "application/json"),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static DingTalkApiClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new DingTalkOptions
        {
            Channels = [DingTalkTestHost.CreateChannelConfig()],
        });
        return new DingTalkApiClient(new HttpClient(handler), options, NullLogger<DingTalkApiClient>.Instance);
    }

    [Fact]
    public async Task GetLegacyAccessTokenAsync_Http500_Throws()
    {
        // DT-4：gettoken HTTP 500 → IsSuccessStatusCode 检查失败 → InvalidOperationException（对齐文件内其他方法错误形态）
        var client = CreateClient(new HttpErrorHandler("/gettoken"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetLegacyAccessTokenAsync(TestAppKey, CancellationToken.None));
    }

    [Fact]
    public async Task GetUserIdAsync_Http500_Throws()
    {
        // gettoken 正常（200）→ getbyunionid 500 → GetUserIdAsync 自身状态检查失败
        var client = CreateClient(new HttpErrorHandler("/topapi/user/getbyunionid"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetUserIdAsync("uni_test_1", TestAppKey, CancellationToken.None));
    }

    [Fact]
    public async Task GetLegacyAccessTokenAsync_Valid_ReturnsToken()
    {
        // 正向回归——状态检查不破坏合法路径
        var client = CreateClient(new HttpErrorHandler("/nonexistent"));
        var token = await client.GetLegacyAccessTokenAsync(TestAppKey, CancellationToken.None);
        Assert.Equal("stub-legacy-token", token);
    }
}