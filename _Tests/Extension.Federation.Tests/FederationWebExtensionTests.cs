using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;
using Xunit;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// FederationWebExtension 端点黑盒冒烟（T3 装配面）——共享宿主 <see cref="FederationEndpointHost"/>
/// （模式 A 全链宿主：ConfigWebAppDomain + ConsumerHostInitializer（[TKWFEnabledExtension(Federation)]）+
/// UseWebSession + FederationWebExtension——DomainHost Root 进程内单例，全项目仅 1 个 Web 宿主；游客帧真实）。
/// <para>领域门面为 Fake（RegisterServices 替换守卫工厂——普通 Scoped，guest.Use 帧内解析）：聚焦端点行为
/// 路由映射 / 匿名帧 / 登录编排参数透传 / FailReason→HTTP（404 CHANNEL_NOT_FOUND）/ JWKS 分发 /
/// <b>子应用消费方委托端点（2026-10-11）</b>——authorize/start（CallbackBaseUrl 服务端可信覆盖）/
/// oauth/callback（GET + 302）/ trust/issue（已认证帧 302 / 未认证 401）/ identity/claim（成功 / 统一 401 防枚举）。
/// 领域 fail-closed（通道启用/密钥）由 Domain 层测试锁定（SsoChannelCollectionTests/SsoSubAppBridgeTests）。</para>
/// <para>RoutePrefix/端点开关的可配性：黑盒需独立宿主（DomainHost 单例不可）——改**配置绑定单测**
/// （IConfiguration → FederationEndpointOptions 绑定链路）+ 配置分层断言（Web Options 不镜像领域配置）。</para>
/// </summary>
public sealed class FederationWebExtensionTests
{
    private const string DefaultPrefix = "/sso";

    // ═══════════════════ 端点冒烟（共享宿主） ═══════════════════

    /// <summary>(a)+(d)：POST /sso/login（无 channelId → 默认通道）——body 参数透传 SsoChannelAuthContext
    /// → Fake 通道 AuthenticateAsync 被调 → 200 SsoChannelAuthResult。</summary>
    [Fact]
    public async Task SsoLogin_DefaultChannel_ParamsPassed_ReturnsResult()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login",
            new SsoLoginWebRequest(new Dictionary<string, string?> { ["code"] = "auth-code-1", ["state"] = "st-1" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SsoChannelAuthResult>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.Equal("ext-100", body.ExternalUserId);
        Assert.Equal(2, body.AuthLevel);
        Assert.True(FederationEndpointHost.Login.DefaultInvoked);          // 无 channelId → LoginDefaultAsync
        Assert.True(FederationEndpointHost.Channel.Authenticated);          // 通道 AuthenticateAsync 被调
        Assert.Equal("auth-code-1", FederationEndpointHost.Channel.LastContext!.Parameters["code"]); // 参数透传
        Assert.Equal("st-1", FederationEndpointHost.Channel.LastContext.Parameters["state"]);
    }

    /// <summary>(d)：POST /sso/login/{channelId}——route 精确选区 → LoginAsync(channelId) → 上下文透传 → 200。</summary>
    [Fact]
    public async Task SsoLogin_RouteChannel_ParamsPassed_ReturnsResult()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/wechat-mp",
            new SsoLoginWebRequest(new Dictionary<string, string?> { ["code"] = "auth-code-2" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SsoChannelAuthResult>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.True(FederationEndpointHost.Login.Invoked);                   // 有 channelId → LoginAsync（非 Default）
        Assert.Equal("wechat-mp", FederationEndpointHost.Login.LastChannelId);
        Assert.Equal("wechat-mp", FederationEndpointHost.Factory.LastChannelId);
        Assert.Equal("auth-code-2", FederationEndpointHost.Channel.LastContext!.Parameters["code"]);
    }

    /// <summary>(b)：未知 channelId → 通道选区失败 → 404 CHANNEL_NOT_FOUND（FailReason HTTP 映射）。</summary>
    [Fact]
    public async Task SsoLogin_UnknownChannel_404()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/unknown-channel",
            new SsoLoginWebRequest(new Dictionary<string, string?> { ["code"] = "x" }));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("CHANNEL_NOT_FOUND", body!.Code);
        Assert.False(FederationEndpointHost.Channel.Authenticated);          // 选区失败不触达通道
    }

    /// <summary>(c)：GET /sso/jwks——信任内核 JWKS 公钥分发（匿名公开）→ 200 + JSON 含 kid。</summary>
    [Fact]
    public async Task Jwks_Get_ReturnsPublicKeys()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync($"{DefaultPrefix}/jwks");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("sso-key-1", body);                                  // kid 分发
        Assert.Contains("P-256", body);
        Assert.True(FederationEndpointHost.Token2.JwksInvoked);
    }

    // ═══════════════════ 子应用消费方委托端点冒烟（ISsoSubAppBridge——2026-10-11） ═══════════════════

    /// <summary>(1)：POST /sso/authorize/start——匿名发起外部认证成功 → 200 {authorize_url, state}；
    /// 断言 Fake 收到请求含 <c>CallbackBaseUrl</c> 服务端可信填充（<c>http://localhost</c>——请求体伪造值被覆盖，防子应用伪造回调域）。</summary>
    [Fact]
    public async Task AuthorizeStart_Success_200_AndServerFillsCallbackBaseUrl()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/authorize/start",
            new AuthorizeStartRequest(
                AppId: "app-1", Mode: "federated", ChannelId: "wechat-mp", State: "sub-state-1",
                Redirect: "https://app.example.com/cb", Scope: null, Parameters: null,
                CallbackBaseUrl: "https://evil.example.com/forged"));   // 请求体伪造值——handler 服务端覆盖

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.True(body!.ContainsKey("authorize_url"));
        Assert.True(body.ContainsKey("state"));
        Assert.Equal("state-ticket", body["state"]);

        // 服务端可信回调基址：请求体 CallbackBaseUrl 被覆盖为 Request.Scheme://Request.Host（TestServer = http://localhost）
        Assert.NotNull(FederationEndpointHost.Bridge.LastStartRequest);
        Assert.Equal("http://localhost", FederationEndpointHost.Bridge.LastStartRequest!.CallbackBaseUrl);
        Assert.Equal("app-1", FederationEndpointHost.Bridge.LastStartRequest.AppId);
        Assert.Equal("wechat-mp", FederationEndpointHost.Bridge.LastStartRequest.ChannelId);
        Assert.Equal("sub-state-1", FederationEndpointHost.Bridge.LastStartRequest.State);
    }

    /// <summary>(2)：authorize/start 失败——FailReason=ORIGIN_NOT_ALLOWED（redirect 白名单拒绝）→ 400 业务码。</summary>
    [Fact]
    public async Task AuthorizeStart_OriginNotAllowed_400()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Bridge.StartResult = new(false, null, null, "ORIGIN_NOT_ALLOWED");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/authorize/start",
            new AuthorizeStartRequest("app-1", "federated", "wechat-mp", "sub-state-1",
                "https://evil.example.com/cb", null, null, ""));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("ORIGIN_NOT_ALLOWED", body!.Code);
    }

    /// <summary>(3)：GET /sso/oauth/{channelId}/callback——OAuth IdP 302 重定向落点（<b>GET</b> 方法）成功 → 302
    /// Location 含 code + state（跳回子应用）；query 参数透传 Fake。</summary>
    [Fact]
    public async Task OAuthCallback_Get_302_LocationContainsCodeAndState()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Bridge.CallbackResult =
            new(true, "https://app.example.com/cb?code=ac-1&state=sub-state-1", "sub-state-1", null);
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync(
            $"{DefaultPrefix}/oauth/wechat-mp/callback?code=wechat-code-1&state=ticket-1");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.StartsWith("https://app.example.com/cb?", location);
        Assert.Contains("code=ac-1", location);
        Assert.Contains("state=sub-state-1", location);

        // query 参数透传（channelId 段 + code/state 参数包）
        Assert.Equal("wechat-mp", FederationEndpointHost.Bridge.LastCallbackChannelId);
        Assert.NotNull(FederationEndpointHost.Bridge.LastCallbackParams);
        Assert.Equal("wechat-code-1", FederationEndpointHost.Bridge.LastCallbackParams!["code"]);
        Assert.Equal("ticket-1", FederationEndpointHost.Bridge.LastCallbackParams["state"]);
    }

    /// <summary>(4a)：POST /sso/trust/issue——已认证帧（测试哨兵中间件 X-Test-Auth-User 写入 Items["DomainUser"]）
    /// → 302 跳回子应用；已认证用户 UserIdString 作为 platformUid 传给门面。</summary>
    [Fact]
    public async Task TrustIssue_AuthenticatedFrame_302()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, $"{DefaultPrefix}/trust/issue")
        {
            Content = JsonContent.Create(new TrustIssueRequest("app-1", "https://app.example.com/cb", "st-1", null)),
        };
        req.Headers.Add("X-Test-Auth-User", "u-100");   // 测试哨兵：模拟已认证帧

        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location?.ToString();
        Assert.Contains("code=ac-2", location);         // Fake 默认结果
        Assert.NotNull(FederationEndpointHost.Bridge.LastTrustIssueRequest);
        Assert.Equal("u-100", FederationEndpointHost.Bridge.LastPlatformUid);
        Assert.Equal("app-1", FederationEndpointHost.Bridge.LastTrustIssueRequest!.AppId);
    }

    /// <summary>(4b)：POST /sso/trust/issue——未认证（无已认证帧）→ 401 UNAUTHENTICATED（不触达门面）。</summary>
    [Fact]
    public async Task TrustIssue_Unauthenticated_401()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/trust/issue",
            new TrustIssueRequest("app-1", "https://app.example.com/cb", "st-1", null));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("UNAUTHENTICATED", body!.Code);
        Assert.Null(FederationEndpointHost.Bridge.LastTrustIssueRequest);   // 未触达门面
    }

    /// <summary>(5a)：POST /sso/identity/claim——子应用兑现成功 → 200 {mode, channel_id, external_user_id}。</summary>
    [Fact]
    public async Task IdentityClaim_Success_200_ReturnsIdentityPayload()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/identity/claim",
            new IdentityClaimRequest("app-1", "secret-1", "ac-1"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, string?>>();
        Assert.NotNull(body);
        Assert.Equal("federated", body!["mode"]);
        Assert.Equal("wx-1", body["channel_id"]);
        Assert.Equal("ext-100", body["external_user_id"]);
        Assert.Null(body["platform_uid"]);
        Assert.NotNull(FederationEndpointHost.Bridge.LastClaimRequest);
        Assert.Equal("app-1", FederationEndpointHost.Bridge.LastClaimRequest!.AppId);
        Assert.Equal("ac-1", FederationEndpointHost.Bridge.LastClaimRequest.Code);
    }

    /// <summary>(5b)：identity/claim 失败——FailReason=AUTHENTICATION_FAILED（credential 鉴权失败，统一防 app_id 枚举）→ 401。</summary>
    [Fact]
    public async Task IdentityClaim_AuthFailed_401()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Bridge.ClaimResult = new(false, null, null, null, null, "AUTHENTICATION_FAILED");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/identity/claim",
            new IdentityClaimRequest("app-1", "wrong-secret", "ac-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("AUTHENTICATION_FAILED", body!.Code);
    }

    // ═══════════════════ 配置绑定（RoutePrefix/端点开关可配性——DomainHost 单例禁多宿主黑盒） ═══════════════════

    [Fact]
    public void ConfigBinding_EndpointOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TKWF:Federation:Web:RoutePrefix"] = "/custom-sso",
            ["TKWF:Federation:Web:LoginEndpointEnabled"] = "false",
            ["TKWF:Federation:Web:JwksEndpointEnabled"] = "false",
            ["TKWF:Federation:Web:EventEndpointEnabled"] = "false",
            ["TKWF:Federation:Web:AuthorizeEndpointEnabled"] = "false",
            ["TKWF:Federation:Web:OauthCallbackEndpointEnabled"] = "false",
            ["TKWF:Federation:Web:TrustIssueEndpointEnabled"] = "false",
            ["TKWF:Federation:Web:IdentityClaimEndpointEnabled"] = "false"
        }).Build();
        var options = new FederationEndpointOptions();
        config.GetSection("TKWF:Federation:Web").Bind(options);

        Assert.Equal("/custom-sso", options.RoutePrefix);
        Assert.False(options.LoginEndpointEnabled);      // 配置覆盖
        Assert.False(options.JwksEndpointEnabled);
        Assert.False(options.EventEndpointEnabled);
        Assert.False(options.AuthorizeEndpointEnabled);        // 子应用消费方 4 开关（2026-10-11）
        Assert.False(options.OauthCallbackEndpointEnabled);
        Assert.False(options.TrustIssueEndpointEnabled);
        Assert.False(options.IdentityClaimEndpointEnabled);
    }

    [Fact]
    public void ConfigLayering_WebOptions_DoNotMirrorDomainConfig()
    {
        // 表现层暴露面 Options 不得镜像领域安全行为（通道启用/密钥路径——防"Web 层关掉连接能力"绕过）
        var webProps = typeof(FederationEndpointOptions).GetProperties();
        Assert.DoesNotContain(webProps, p => p.Name == nameof(FederationChannelRegistryOptions.SecretEncryptionKeyPath));
        Assert.DoesNotContain(webProps, p => p.Name == nameof(FederationChannelRegistryOptions.IsProduction));
        // 领域 Options 不得含暴露面属性（RoutePrefix——防双源）
        var domainProps = typeof(FederationChannelRegistryOptions).GetProperties();
        Assert.DoesNotContain(domainProps, p => p.Name == nameof(FederationEndpointOptions.RoutePrefix));
    }
}

/// <summary>authorize/start 响应（FederationWebExtension JSON——snake_case 契约）。</summary>
public sealed record AuthorizeStartResponse(string? authorize_url, string? state);

/// <summary>identity/claim 响应（FederationWebExtension JSON——snake_case 契约）。</summary>
public sealed record IdentityClaimResponse(string? mode, string? channel_id, string? external_user_id, string? platform_uid);
