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
/// 路由映射 / 匿名帧 / 登录编排参数透传 / FailReason→HTTP（404 CHANNEL_NOT_FOUND）/ JWKS 分发。
/// 领域 fail-closed（通道启用/密钥）由 Domain 层测试锁定（SsoChannelCollectionTests/SsoChannelRegistryTests）。</para>
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

    // ═══════════════════ 配置绑定（RoutePrefix/端点开关可配性——DomainHost 单例禁多宿主黑盒） ═══════════════════

    [Fact]
    public void ConfigBinding_EndpointOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TKWF:Federation:Web:RoutePrefix"] = "/custom-sso",
            ["TKWF:Federation:Web:LoginEndpointEnabled"] = "false",
            ["TKWF:Federation:Web:JwksEndpointEnabled"] = "false",
            ["TKWF:Federation:Web:EventEndpointEnabled"] = "false"
        }).Build();
        var options = new FederationEndpointOptions();
        config.GetSection("TKWF:Federation:Web").Bind(options);

        Assert.Equal("/custom-sso", options.RoutePrefix);
        Assert.False(options.LoginEndpointEnabled);      // 配置覆盖
        Assert.False(options.JwksEndpointEnabled);
        Assert.False(options.EventEndpointEnabled);
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