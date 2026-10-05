using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// AuthCenterWebExtension 端点黑盒冒烟（V0.6.0 归层迭代 T4/T5）——共享宿主 <see cref="AuthCenterEndpointHost"/>
/// （模式 A 全链宿主：ConfigWebAppDomain + ConsumerHostInitializer + UseWebSession + AuthCenterWebExtension——
/// DomainHost Root 进程内单例，全项目仅 1 个 Web 宿主，JwtAuth 顺序冒烟共用；游客帧真实）。
/// <para>领域门面为 Fake（RegisterServices 替换守卫工厂——普通 Scoped，guest.Use 帧内解析）：聚焦端点行为
/// 路由映射 / 匿名帧 / 认证登出 / FailReason→HTTP / 免认证。领域 fail-closed（EnabledAuthTypes）由 Domain 层
/// 测试锁定（AuthenticationProductionPathTests）。</para>
/// <para>RoutePrefix/端点开关的可配性：黑盒需独立宿主（DomainHost 单例不可）——改**配置绑定单测**
/// （IConfiguration → AuthCenterEndpointOptions 绑定链路）+ 配置分层断言（Web Options 不镜像领域配置）。</para>
/// </summary>
public sealed class AuthCenterWebExtensionTests
{
    private const string DefaultPrefix = "/api/auth";

    // ═══════════════════ 端点冒烟（共享宿主） ═══════════════════

    [Fact]
    public async Task SmsLogin_Success_ReturnsTokenResponse()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/sms", new SmsLoginRequest("13800138000", "123456"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.Equal("at-1", body!.AccessToken);
        Assert.Equal("Bearer", body.TokenType);
        Assert.Equal(7200, body.ExpiresIn);
        Assert.True(AuthCenterEndpointHost.SmsLogin.Invoked);
    }

    [Fact]
    public async Task SmsLogin_MissingParams_400()
    {
        // 400 已证明 handler 参数校验拦截（若门面被调会走 Fake 成功 200——无需断言 Invoked，共享 Fake 状态跨用例残留）
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/sms", new SmsLoginRequest("", ""));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task SmsLogin_FailReason_MapsTo400()
    {
        // FailReason → 400 + code（HTTP 映射）；末尾还原默认 Result（共享 Fake——串行下防污染）
        AuthCenterEndpointHost.SmsLogin.Result = new LoginResult(false, null, null, "SMS_CODE_MISMATCH");
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/sms", new SmsLoginRequest("13800138000", "000000"));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("SMS_CODE_MISMATCH", body!.Code);
        }
        finally
        {
            AuthCenterEndpointHost.SmsLogin.Result = new LoginResult(true, "u_1", new TokenIssueResult("at-1", "rt-1", 7200), null);
        }
    }

    [Fact]
    public async Task SmsSendCode_ValidScene_204()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/sms/send-code", new SendSmsCodeRequest("13800138000", SmsScenes.Login));

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.True(AuthCenterEndpointHost.SmsVerification.SendCodeInvoked);
    }

    [Fact]
    public async Task SmsSendCode_InvalidScene_400()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/sms/send-code", new SendSmsCodeRequest("13800138000", "hack"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("INVALID_SCENE", body!.Code);
        Assert.False(AuthCenterEndpointHost.SmsVerification.SendCodeInvoked);
    }

    [Fact]
    public async Task WechatLogin_Success_ReturnsTokenResponse()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/wechat", new WechatLoginRequest("wx-code", "snsapi_base"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.Equal("at-1", body!.AccessToken);
        Assert.True(AuthCenterEndpointHost.WechatLogin.Invoked);
    }

    [Fact]
    public async Task Refresh_Success_ReturnsTokenResponse()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/refresh", new RefreshRequest("rt-1"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.Equal("at-2", body!.AccessToken);
        Assert.True(AuthCenterEndpointHost.Token.RefreshInvoked);
    }

    [Fact]
    public async Task Logout_WithBearer_204()
    {
        // 已认证端点：共享宿主已注册 FakeTokenVerifier（Bearer 验签 → RestoreUser 替换已认证帧 +
        // 中间件写 Items[AuthCenter.TokenValidationResult]）→ handler 取 jti 撤销
        var client = AuthCenterEndpointHost.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "valid-token");

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/logout", new { });

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.Equal("jti-test", AuthCenterEndpointHost.Token.RevokedJti);
    }

    [Fact]
    public async Task TicketExchange_Success_ReturnsTokenResponse()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/ticket/exchange",
            new TicketExchangeRequest("t-1", "verifier", "app-1", "state-1"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.Equal("at-3", body!.AccessToken);
        Assert.True(AuthCenterEndpointHost.Ticket.ExchangeInvoked);
    }

    // ═══════════════════ 配置绑定（RoutePrefix/端点开关可配性——DomainHost 单例禁多宿主黑盒） ═══════════════════

    [Fact]
    public void ConfigBinding_EndpointOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TKWF:AuthCenter:Web:RoutePrefix"] = "/custom",
            ["TKWF:AuthCenter:Web:SmsLoginEndpointEnabled"] = "false"
        }).Build();
        var options = new AuthCenterEndpointOptions();
        config.GetSection("TKWF:AuthCenter:Web").Bind(options);

        Assert.Equal("/custom", options.RoutePrefix);
        Assert.False(options.SmsLoginEndpointEnabled);      // 配置覆盖
        Assert.True(options.SmsSendCodeEndpointEnabled);    // 未配置保持默认
    }

    [Fact]
    public void ConfigLayering_WebOptions_DoNotMirrorDomainConfig()
    {
        // 表现层暴露面 Options 不得镜像领域安全行为（EnabledAuthTypes/LoginProtection——防"Web 层关掉频控/口令校验"绕过）
        var webProps = typeof(AuthCenterEndpointOptions).GetProperties();
        Assert.DoesNotContain(webProps, p => p.Name == nameof(AuthCenterOptions.EnabledAuthTypes));
        Assert.DoesNotContain(webProps, p => p.Name == nameof(AuthCenterOptions.LoginProtection));

        // 领域 Options 不得含暴露面属性（RoutePrefix——防双源）
        var domainProps = typeof(AuthCenterOptions).GetProperties();
        Assert.DoesNotContain(domainProps, p => p.Name == nameof(AuthCenterEndpointOptions.RoutePrefix));
    }
}
