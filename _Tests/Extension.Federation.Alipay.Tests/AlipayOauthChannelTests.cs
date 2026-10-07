using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.Alipay.Tests;

/// <summary>
/// <see cref="AlipayOauthChannel"/>（alipay_oauth——支付宝授权身份获取）测试——生产路径宿主
/// （真实 DI + <c>AddAlipayFederationChannels</c> + 探针门面帧内枚举）+ Stub gateway.do（RSA2 签名响应）。
/// <para>验收映射（N4 §五）：F2（code→user_id 正 / code 缺拒 / redirect_uri 缺拒 / 换取失败负）+
/// F3（RSA2 双向签名——桩真实签名验签通过 / 篡改签名拒——信任根正负）+ F4（通道注册进集合——探针枚举）+
/// F6（生产路径）+ 协议结构断言（method/grant_type/code 顶层、无 biz_content、timestamp 格式、sign 存在）。</para>
/// </summary>
public class AlipayOauthChannelTests : AlipayChannelTestHost
{
    private const string TestAppId = "test-appid";
    private const string TestChannelId = "alipay-main";

    private async Task<(DomainUser<TestUserInfo> User, AlipayOauthChannel Channel)> Setup(
        bool failTokenApi = false, bool tamperSignature = false)
    {
        Handler = AlipayTestHost.CreateStubHandler(failTokenApi: failTokenApi, tamperSignature: tamperSignature);
        ConfigureOptions = o => o.Channels = [AlipayTestHost.CreateChannelConfig(channelId: TestChannelId, appId: TestAppId)];
        var (_, user) = BindTestScope();
        // v0.3.0：经 ISsoChannelFactory 按 channelId 构造真实实例（凭证从 registry 懒加载）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "alipay_oauth");
        return (user, Assert.IsType<AlipayOauthChannel>(channel));
    }

    [Fact]
    public async Task ChannelType_IsAlipayOauth_AndChannelId_FromFactory()
    {
        ConfigureOptions = o => o.Channels = [AlipayTestHost.CreateChannelConfig(channelId: TestChannelId, appId: TestAppId)];
        var (_, user) = BindTestScope();
        // 探针枚举集合（模板实例——ChannelType 标识注册）……
        var probe = user.Use<IChannelProbe>();
        var template = Assert.Single(probe.Channels, c => c.ChannelType == "alipay_oauth");
        Assert.Equal("alipay_oauth", template.ChannelType);
        // ……真实实例经工厂构造（ChannelId = 注册表选区键）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "alipay_oauth");
        Assert.NotNull(channel);
        Assert.Equal(TestChannelId, channel!.ChannelId);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCode_ReturnsUserId_Success()
    {
        using var _ = this;
        var (_, channel) = await Setup();
        var context = AlipayTestHost.CreateContext(
            ("code", "alipay-oauth-code-123"),   // 支付宝回调 auth_code 经装配层映射为 "code" 键（对齐 QQ 先例）
            ("redirect_uri", AlipayTestHost.TestRedirectUri));

        var result = await channel.AuthenticateAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(AlipayTestHost.TestUserId, result.ExternalUserId); // Stub 返回 user_id
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel); // 支付宝平台便捷
    }

    [Fact]
    public async Task AuthenticateAsync_MissingCode_Fails_ALIPAY_CODE_REQUIRED()
    {
        using var _ = this;
        var (_, channel) = await Setup();

        var result = await channel.AuthenticateAsync(
            AlipayTestHost.CreateContext(
                ("state", "abc"),
                ("redirect_uri", AlipayTestHost.TestRedirectUri)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ALIPAY_CODE_REQUIRED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingRedirectUri_Fails_ALIPAY_REDIRECT_URI_REQUIRED()
    {
        // N4 P1-2：redirect_uri 必填（防开放重定向——与支付宝控制台回调配置一致比对归装配层；通道仅校验注入存在）
        using var _ = this;
        var (_, channel) = await Setup();

        var result = await channel.AuthenticateAsync(
            AlipayTestHost.CreateContext(("code", "alipay-oauth-code-123")),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ALIPAY_REDIRECT_URI_REQUIRED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_TokenApiError_Fails_WithSubCode()
    {
        // 支付宝 oauth.token 业务错误（code=40004/sub_code=isv.code-invalid——sub_code 优先）→ 失败 + FailReason 携带 sub_code
        using var _ = this;
        var (_, channel) = await Setup(failTokenApi: true);

        var result = await channel.AuthenticateAsync(
            AlipayTestHost.CreateContext(
                ("code", "stale-code"),
                ("redirect_uri", AlipayTestHost.TestRedirectUri)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Contains("isv.code-invalid", result.FailReason); // sub_code 优先（librarian 协议事实）
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_ResponseSignatureInvalid_Fails()
    {
        // 信任根负路径（N4 §3.2 强双向签名）：桩用错误私钥签名 → 支付宝公钥验签失败 → 拒绝（FAIL_SIGNATURE）
        using var _ = this;
        var (_, channel) = await Setup(tamperSignature: true);

        var result = await channel.AuthenticateAsync(
            AlipayTestHost.CreateContext(
                ("code", "alipay-oauth-code-123"),
                ("redirect_uri", AlipayTestHost.TestRedirectUri)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Contains("FAIL_SIGNATURE", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }
}

/// <summary>
/// <see cref="AlipayApiClient"/> 出站客户端分层单测——直构（HttpClient + Options + AlipaySignService + NullLogger），不经宿主。
/// <para>验收映射（N4 §五）：F3（RSA2 双向签名——响应验签通过/篡改拒）+ 协议结构（method/grant_type/code 顶层、
/// 无 biz_content、timestamp 格式、sign 存在、charset 查询串——librarian §4）+ snake_case 字段裁剪（N4 §二）。</para>
/// </summary>
public class AlipayApiClientTests
{
    private const string TestAppId = "test-appid";

    private AlipayApiClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new AlipayOptions
        {
            Channels = [AlipayTestHost.CreateChannelConfig(appId: TestAppId)],
        });
        var http = new HttpClient(handler);
        return new AlipayApiClient(http, options, new AlipaySignService(), NullLogger<AlipayApiClient>.Instance);
    }

    [Fact]
    public async Task GetAccessTokenAsync_TopLevelParams_NoBizContent()
    {
        // 协议结构断言（librarian §4/§二）：oauth.token 无 biz_content——grant_type/code 为顶层参数；
        // app_id 承载身份（无 client_id/client_secret）；sign_type=RSA2；timestamp yyyy-MM-dd HH:mm:ss；sign 存在；charset 在查询串
        var captured = new TaskCompletionSource<System.Net.Http.HttpRequestMessage>();
        var handler = new CapturingHandler(m => captured.TrySetResult(m), AlipayTestHost.CreateStubHandler());
        var client = CreateClient(handler);

        var token = await client.GetAccessTokenAsync(TestAppId, "alipay-oauth-code-123", CancellationToken.None);

        Assert.Equal("stub-access-token", token.AccessToken);
        Assert.Equal(AlipayTestHost.TestUserId, token.UserId);
        Assert.Equal(3600, token.ExpiresInSeconds);
        Assert.Equal("stub-refresh-token", token.RefreshToken);

        var req = await captured.Task.WaitAsync(System.TimeSpan.FromSeconds(5));
        var body = await req.Content!.ReadAsStringAsync();
        Assert.Contains("method=alipay.system.oauth.token", body);
        Assert.Contains("grant_type=authorization_code", body);   // 顶层参数（非 biz_content）
        Assert.Contains("code=alipay-oauth-code-123", body);      // 顶层参数
        Assert.DoesNotContain("biz_content", body);               // ⚠️ 无 biz_content（协议事实）
        Assert.DoesNotContain("client_secret", body);             // ⚠️ 无 client_id/client_secret（RSA2 签名认证）
        Assert.Contains("app_id=test-appid", body);
        Assert.Contains("sign_type=RSA2", body);
        Assert.Contains("sign=", body);                           // 商户私钥签名已赋 sign
        var ts = GetFormParam(body, "timestamp");
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", ts); // timestamp 格式（librarian §4）
        Assert.Contains("charset=utf-8", req.RequestUri!.Query);  // charset 在 URL 查询串
    }

    [Fact]
    public async Task GetAccessTokenAsync_ApiError_Throws_WithSubCode()
    {
        // 业务错误 code=40004/sub_code=isv.code-invalid（验签通过但业务失败）→ 抛 InvalidOperationException 携 sub_code
        var client = CreateClient(AlipayTestHost.CreateStubHandler(failTokenApi: true));
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => client.GetAccessTokenAsync(TestAppId, "stale-code", CancellationToken.None));
        Assert.Contains("isv.code-invalid", ex.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_TamperedSignature_Throws_FailSignature()
    {
        // 信任根负路径：桩错误私钥签名 → 验签失败 → 抛 FAIL_SIGNATURE（拒绝信任，不解析业务体）
        var client = CreateClient(AlipayTestHost.CreateStubHandler(tamperSignature: true));
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => client.GetAccessTokenAsync(TestAppId, "code-1", CancellationToken.None));
        Assert.Contains("FAIL_SIGNATURE", ex.Message);
    }

    [Fact]
    public async Task GetUserInfoAsync_ParsesSnakeCase_Fields()
    {
        // alipay.user.info.share snake_case 字段裁剪（N4 §二）：nick_name/avatar/province/city/gender + user_id/open_id
        var client = CreateClient(AlipayTestHost.CreateStubHandler());
        var info = await client.GetUserInfoAsync(TestAppId, "stub-access-token", CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal(AlipayTestHost.TestUserId, info.UserId);
        Assert.Equal("stub-open-id", info.OpenId);
        Assert.Equal("支付宝测试用户", info.Nickname);      // nick_name 映射
        Assert.NotNull(info.Avatar);
        Assert.Equal("浙江省", info.Province);
        Assert.Equal("杭州市", info.City);
        Assert.Equal("M", info.Gender);
    }

    [Fact]
    public async Task GetUserInfoAsync_MissingAccessToken_Throws()
    {
        var client = CreateClient(AlipayTestHost.CreateStubHandler());
        await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => client.GetUserInfoAsync(TestAppId, "", CancellationToken.None));
    }

    [Fact]
    public void BuildAuthorizeUrl_EncodesParams_AndStateOptional()
    {
        // authorize 构造（归装配层——N4 P1-1）：app_id/scope/redirect_uri URL Encode；state 可选（CSRF 归装配层）
        var url = AlipayApiClient.BuildAuthorizeUrl(TestAppId, AlipayTestHost.TestRedirectUri, "auth_base", "abc123");
        Assert.StartsWith("https://openauth.alipay.com/oauth2/publicAppAuthorize.htm?", url);
        Assert.Contains($"app_id={TestAppId}", url);
        Assert.Contains("scope=auth_base", url);
        Assert.Contains($"redirect_uri={System.Uri.EscapeDataString(AlipayTestHost.TestRedirectUri)}", url);
        Assert.Contains("state=abc123", url);

        var noState = AlipayApiClient.BuildAuthorizeUrl(TestAppId, AlipayTestHost.TestRedirectUri, "auth_user");
        Assert.DoesNotContain("state=", noState);
    }

    /// <summary>捕获首个请求 + 透传 stub（断言出站协议结构——librarian 协议事实核实结论落测试）。
    /// 用 <see cref="System.Net.Http.DelegatingHandler"/>（框架内部转发 <see cref="System.Net.Http.DelegatingHandler.InnerHandler"/>，免 protected 访问）。</summary>
    private sealed class CapturingHandler : System.Net.Http.DelegatingHandler
    {
        private readonly Action<System.Net.Http.HttpRequestMessage> _onRequest;

        public CapturingHandler(Action<System.Net.Http.HttpRequestMessage> onRequest, HttpMessageHandler inner)
        {
            _onRequest = onRequest;
            InnerHandler = inner;
        }

        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            _onRequest(request);
            return await base.SendAsync(request, ct);
        }
    }

    /// <summary>表单参数提取（FormUrlEncodedContent——%XX 编码 + 空格为 '+' 约定；手拆避免 System.Web 依赖）。</summary>
    private static string GetFormParam(string form, string name)
    {
        foreach (var pair in form.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == name)
                return System.Uri.UnescapeDataString(kv[1]).Replace('+', ' ');
        }
        return "";
    }
}
