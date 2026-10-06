using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.QQ.Tests;

/// <summary>
/// <see cref="QqOauthChannel"/>（qq_oauth——QQ 网页授权身份获取）测试——生产路径宿主
/// （真实 DI + <c>AddQqFederationChannels</c> + 探针门面帧内枚举）+ Stub QQ API（code→access_token→/me→openid 链）。
/// <para>验收映射（N3 §五）：F2（code→openid 正 / code 缺拒 / <b>redirect_uri 不一致拒 P1-4</b> / openid 换取失败负）+
/// F3（redirect_uri 一致性——出站链携带 + 服务端比对模拟）+ F4（通道注册进集合——探针枚举）+ F6（生产路径）。</para>
/// </summary>
public class QqOauthChannelTests : QqChannelTestHost
{
    private const string TestAppId = "test-appid";
    private const string TestOpenId = "test_openid_1";

    private (DomainUser<TestUserInfo> User, QqOauthChannel Channel) Setup(bool failOauth = false, bool failMe = false, bool redirectMismatch = false)
    {
        Handler = QqTestHost.CreateStubHandler(TestOpenId, failOauth: failOauth, failMe: failMe, redirectMismatch: redirectMismatch);
        ConfigureOptions = o => o.Channels = [QqTestHost.CreateChannelConfig(appId: TestAppId)];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.IsType<QqOauthChannel>(
            Assert.Single(probe.Channels, c => c.ChannelType == "qq_oauth"));
        return (user, channel);
    }

    [Fact]
    public void ChannelType_IsQqOauth_AndChannelIdFromOptions()
    {
        ConfigureOptions = o => o.Channels = [QqTestHost.CreateChannelConfig(channelId: "qq-main", appId: TestAppId)];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "qq_oauth");
        Assert.Equal("qq_oauth", channel.ChannelType);
        Assert.Equal("qq-main", channel.ChannelId); // ChannelId = 配置 channel id（Options.Channels 选区）
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCode_ReturnsOpenId_Success()
    {
        using var _ = this;
        var (_, channel) = Setup();
        var context = QqTestHost.CreateContext(
            ("code", "qq-oauth-code-123"),
            ("redirect_uri", QqTestHost.TestRedirectUri));

        var result = await channel.AuthenticateAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId); // Stub /me 返回 openid
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel); // QQ 平台便捷
    }

    [Fact]
    public async Task AuthenticateAsync_MissingCode_Fails_QQ_CODE_REQUIRED()
    {
        using var _ = this;
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(
            QqTestHost.CreateContext(
                ("state", "abc"),
                ("redirect_uri", QqTestHost.TestRedirectUri)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("QQ_CODE_REQUIRED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingRedirectUri_Fails_QQ_REDIRECT_URI_REQUIRED()
    {
        // P1-4：redirect_uri 必填——缺省拒（防 code 窃取后换 token 攻击面：无 redirect_uri 不发出换取）
        using var _ = this;
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(
            QqTestHost.CreateContext(("code", "qq-oauth-code-123")),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("QQ_REDIRECT_URI_REQUIRED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_RedirectUriMismatch_Fails_QQ_REJECTs()
    {
        // P1-4 验收「redirect_uri 不一致拒」：QQ 服务端比对失败（stub 模拟 error 100030）→ 通道失败
        using var _ = this;
        var (_, channel) = Setup(redirectMismatch: true);

        var result = await channel.AuthenticateAsync(
            QqTestHost.CreateContext(
                ("code", "qq-oauth-code-123"),
                ("redirect_uri", "https://evil.example.com/callback")),  // 与非 callback 一致，服务端拒
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Contains("100030", result.FailReason);   // QQ error 码（记号：服务端一致性拒绝路径）
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_TokenApiError_Fails_WithMessage()
    {
        // QQ 出站换取链第一步（token 端点）返回 error 模型错误（code 非法/已消费）→ 失败 + FailReason 携带错误消息
        using var _ = this;
        var (_, channel) = Setup(failOauth: true);

        var result = await channel.AuthenticateAsync(
            QqTestHost.CreateContext(
                ("code", "stale-code"),
                ("redirect_uri", QqTestHost.TestRedirectUri)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Contains("100016", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_MeApiError_Fails_WithMessage()
    {
        // 换取链第二步（/me 端点）失败——token 有效但 openid 换取被拒
        using var _ = this;
        var (_, channel) = Setup(failMe: true);

        var result = await channel.AuthenticateAsync(
            QqTestHost.CreateContext(
                ("code", "qq-oauth-code-123"),
                ("redirect_uri", QqTestHost.TestRedirectUri)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Contains("100016", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }
}

/// <summary>
/// <see cref="QqApiClient"/> 出站客户端分层单测——直构（HttpClient + Options + NullLogger），不经宿主。
/// <para>验收映射（N3 §五）：F2（redirect_uri 一致性出站携带——URL 断言）/ F3（获取链失败抛错）+
/// 协议结构（token fmt=json / /me 默认 JSONP 剥壳 / get_user_info ret-msg 裁剪——N3 §二）。</para>
/// </summary>
public class QqApiClientTests
{
    private const string TestAppId = "test-appid";
    private const string TestAppSecret = "test-secret";
    private const string TestOpenId = "test_openid_1";

    private QqApiClient CreateClient(HttpMessageHandler handler, bool enableUnionId = false)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new QqOptions
        {
            Channels = [QqTestHost.CreateChannelConfig(appId: TestAppId, appSecret: TestAppSecret, enableUnionId: enableUnionId)],
        });
        var http = new HttpClient(handler);
        return new QqApiClient(http, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<QqApiClient>.Instance);
    }

    [Fact]
    public async Task GetAccessTokenAsync_SendsFmtJson_AndRedirectUri()
    {
        // 捕获出站请求断言：fmt=json 恒传（QQ 默认 x-www-form-urlencoded 非 JSON —— 必须强制）+ redirect_uri 一致性出站携带（P1-4）
        var captured = new TaskCompletionSource<System.Net.Http.HttpRequestMessage>();
        var handler = new CapturingHandler(m => captured.TrySetResult(m), QqTestHost.CreateStubHandler(TestOpenId));
        var client = CreateClient(handler);

        var token = await client.GetAccessTokenAsync(TestAppId, "code-1", QqTestHost.TestRedirectUri);

        Assert.Equal("stub-access-token", token);
        var req = await captured.Task.WaitAsync(System.TimeSpan.FromSeconds(5));
        Assert.Contains("fmt=json", req.RequestUri!.Query);
        Assert.Contains($"redirect_uri={System.Uri.EscapeDataString(QqTestHost.TestRedirectUri)}", req.RequestUri.Query);
        Assert.Contains($"grant_type=authorization_code", req.RequestUri.Query);
        Assert.Contains("client_secret=test-secret", req.RequestUri.Query);
    }

    [Fact]
    public async Task GetAccessTokenAsync_NullRedirectUri_Throws()
    {
        // P1-4：redirect_uri 缺省 → 拒绝发出换取（防 code 窃取换 token）
        var client = CreateClient(QqTestHost.CreateStubHandler(TestOpenId));
        await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => client.GetAccessTokenAsync(TestAppId, "code-1", "", CancellationToken.None));
    }

    [Fact]
    public async Task GetAccessTokenAsync_ApiError_Throws_WithMessage()
    {
        var client = CreateClient(QqTestHost.CreateStubHandler(TestOpenId, failOauth: true));
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => client.GetAccessTokenAsync(TestAppId, "stale", QqTestHost.TestRedirectUri, CancellationToken.None));
        Assert.Contains("100016", ex.Message);
    }

    [Fact]
    public async Task GetMeAsync_ReturnsOpenId_AndUnionIdWhenEnabled()
    {
        // /me 换取：默认仅 openid；EnableUnionId=true 时 unionid=1 请求 + 结果含 unionid（P1-2/N3 §3.3）
        var client = CreateClient(QqTestHost.CreateStubHandler(TestOpenId, unionId: "test_unionid_1"), enableUnionId: true);
        var me = await client.GetMeAsync(TestAppId, "stub-access-token", CancellationToken.None);

        Assert.Equal(TestOpenId, me.OpenId);
        Assert.Equal("test_unionid_1", me.UnionId);
    }

    [Fact]
    public async Task GetMeAsync_UnionIdDisabled_ReturnsNull()
    {
        var client = CreateClient(QqTestHost.CreateStubHandler(TestOpenId, unionId: "test_unionid_1"), enableUnionId: false);
        var me = await client.GetMeAsync(TestAppId, "stub-access-token", CancellationToken.None);

        Assert.Equal(TestOpenId, me.OpenId);
        Assert.Null(me.UnionId); // EnableUnionId=false 不出 unionid=1 请求
    }

    [Fact]
    public async Task GetMeAsync_ApiError_Throws()
    {
        var client = CreateClient(QqTestHost.CreateStubHandler(TestOpenId, failMe: true));
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => client.GetMeAsync(TestAppId, "bad-token", CancellationToken.None));
        Assert.Contains("100016", ex.Message);
    }

    [Fact]
    public async Task GetUserInfoAsync_ReturnsTrimmed_OnSuccess()
    {
        var client = CreateClient(QqTestHost.CreateStubHandler(TestOpenId));
        var info = await client.GetUserInfoAsync(TestAppId, "stub-access-token", TestOpenId, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(TestOpenId, info.OpenId);
        Assert.Equal("QQ 测试用户", info.Nickname);
        Assert.Contains("/40", info.Avatar!);     // figureurl_qq_1 优先
        Assert.Equal("男", info.Gender);
        Assert.Equal("广东", info.Province);
        Assert.Equal("深圳", info.City);
    }

    [Fact]
    public async Task GetUserInfoAsync_RetError_ReturnsNull()
    {
        // get_user_info 错误模型 ret/msg（区别于 error/error_description——N3 §二 ）→ 降级 null
        var client = CreateClient(QqTestHost.CreateStubHandler(TestOpenId, failUserInfo: true));
        var info = await client.GetUserInfoAsync(TestAppId, "stub-access-token", TestOpenId, CancellationToken.None);
        Assert.Null(info);
    }

    [Fact]
    public void StripJsonp_RemovesCallbackShell()
    {
        // /me 端点默认 JSONP（官方原文 callback(...) 形态）——剥壳兜底（即便恒传 fmt=json，官方 PHP 实证 token 出错也可能 JSONP）
        Assert.Equal("{\"client_id\":\"a\",\"openid\":\"b\"}", QqApiClient.StripJsonp("callback( {\"client_id\":\"a\",\"openid\":\"b\"} );"));
        Assert.Equal("{\"client_id\":\"a\"}", QqApiClient.StripJsonp("callback( {\"client_id\":\"a\"} )"));
        Assert.Equal("{\"ret\":0}", QqApiClient.StripJsonp("{\"ret\":0}")); // 纯 JSON 原样
    }

    /// <summary>捕获首个请求 + 透传 stub（断言出站 URL 形态——协议事实核实结论落测试）。
    /// 用 <see cref="System.Net.Http.DelegatingHandler"/>（框架内部转发 <see cref="System.Net.Http.DelegatingHandler.InnerHandler"/>，免 protected 访问）。</summary>
    private sealed class CapturingHandler : System.Net.Http.DelegatingHandler
    {
        private readonly Action<System.Net.Http.HttpRequestMessage> _onRequest;

        public CapturingHandler(Action<System.Net.Http.HttpRequestMessage> onRequest, HttpMessageHandler inner)
        {
            _onRequest = onRequest;
            InnerHandler = inner;
        }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            _onRequest(request);
            return base.SendAsync(request, ct);
        }
    }
}