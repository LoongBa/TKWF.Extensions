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
        AuthCenterEndpointHost.SmsVerification.Reset(); // 共享宿主——负向断言前置重置（V0.8.0）
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/sms/send-code", new SendSmsCodeRequest("13800138000", "hack"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("INVALID_SCENE", body!.Code);
        Assert.False(AuthCenterEndpointHost.SmsVerification.SendCodeInvoked);
    }

    [Fact]
    public async Task ExternalLogin_Success_ReturnsTokenResponse()
    {
        // T5（2026-10-09）：POST /login/external/{channelType}（替代 /login/wechat）——body = 参数字典
        AuthCenterEndpointHost.ExternalLogin.GuardExceptionMessage = null;
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/external/wechat_oauth",
            new Dictionary<string, string?> { ["code"] = "wx-code", ["channel_id"] = "fake-mp" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.Equal("at-1", body!.AccessToken);
        Assert.True(AuthCenterEndpointHost.ExternalLogin.Invoked);
        Assert.Equal("wechat_oauth", AuthCenterEndpointHost.ExternalLogin.LastChannelType);       // 路径参数透传
        Assert.Equal("wx-code", AuthCenterEndpointHost.ExternalLogin.LastParameters!["code"]);   // body 参数透传
    }

    [Fact]
    public async Task ExternalLogin_FederationNotAssembled_503()
    {
        // fail-hard（P7）：Federation 未装配 → 守卫异常（领域架构守卫）→ 503 EXTERNAL_IDP_NOT_CONFIGURED
        AuthCenterEndpointHost.ExternalLogin.GuardExceptionMessage = "领域架构守卫：AddConstructibleService 实现类（ExternalIdpLoginService）帧外解析";
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/external/wechat_oauth",
                new Dictionary<string, string?> { ["code"] = "wx-code", ["channel_id"] = "fake-mp" });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("EXTERNAL_IDP_NOT_CONFIGURED", body!.Code);
        }
        finally
        {
            AuthCenterEndpointHost.ExternalLogin.GuardExceptionMessage = null;   // 共享宿主还原
        }
    }

    [Fact]
    public async Task ExternalLogin_MissingCode_400()
    {
        AuthCenterEndpointHost.ExternalLogin.GuardExceptionMessage = null;
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/external/wechat_oauth",
            new Dictionary<string, string?> { ["channel_id"] = "fake-mp" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("INVALID_ARGUMENT", body!.Code);
    }

    [Fact]
    public async Task ExternalLogin_FailReason_MapsTo400()
    {
        // FailReason → 400 + code（HTTP 映射）；末尾还原默认 Result（共享 Fake——串行下防污染）
        AuthCenterEndpointHost.ExternalLogin.GuardExceptionMessage = null;
        AuthCenterEndpointHost.ExternalLogin.Result = new LoginResult(false, null, null, "EXTERNAL_AUTH_FAILED");
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/external/qq_oauth",
                new Dictionary<string, string?> { ["code"] = "qq-code", ["channel_id"] = "qq-mp" });

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("EXTERNAL_AUTH_FAILED", body!.Code);
        }
        finally
        {
            AuthCenterEndpointHost.ExternalLogin.Result = new LoginResult(true, "u_1", new TokenIssueResult("at-1", "rt-1", 7200), null);
        }
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

    // ═══════════════════ V0.8.0 认证 API 补全（3 端点冒烟——共享宿主模式 A） ═══════════════════

    [Fact]
    public async Task Verify_WithBearer_ReturnsSnapshot()
    {
        // 已认证内省：判别器 = 中间件验签产物 Items[TokenValidationResult]——FakeTokenVerifier 验签 →
        // Items 写入 → handler 映射快照（零二次验签）
        var client = AuthCenterEndpointHost.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "valid-token");

        var resp = await client.GetAsync($"{DefaultPrefix}/verify");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<VerifyResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Valid);
        Assert.Equal("u-100", body.UserId);                     // FakeTokenVerifier 载荷
        Assert.Equal(AuthTypes.Sms, body.AuthType);
        Assert.Equal((int)AuthLevel.Phone, body.AuthLevel);
        Assert.Equal("jti-test", body.Jti);
        Assert.True(body.Exp > 0);                              // ExpiresAtUtc → unix 秒
    }

    [Fact]
    public async Task Verify_NoBearer_401()
    {
        // 无效/缺失 Bearer → 中间件透传匿名（Items 无 TokenValidationResult）→ handler 401 显式拒绝
        // （非 RFC 7662 200{active:false}——已认证内省语义，README/指南标注）
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync($"{DefaultPrefix}/verify");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Grants_WithBearer_ReturnsList()
    {
        // /grants：已认证帧 + app_id → FakeAuthGrantQueryService 返回 app-1 授权行 → GrantsResponse
        var client = AuthCenterEndpointHost.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "valid-token");

        var resp = await client.GetAsync($"{DefaultPrefix}/grants?app_id=app-1");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<GrantsResponse>();
        Assert.NotNull(body);
        var grant = Assert.Single(body!.Grants);
        Assert.Equal("app-1", grant.AppId);
        Assert.Equal(new[] { "a", "b" }, grant.Scopes);         // 逗号串拆分（Web 层 GrantView 构造器）
        Assert.Equal(AuthGrantSources.Login, grant.Source);
        Assert.Equal(0, grant.Status);
        Assert.True(AuthCenterEndpointHost.Grants.Invoked);
    }

    [Fact]
    public async Task Grants_NoBearer_401()
    {
        AuthCenterEndpointHost.Grants.Reset(); // 共享宿主——负向断言前置重置（V0.8.0）
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync($"{DefaultPrefix}/grants");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.False(AuthCenterEndpointHost.Grants.Invoked);
    }

    [Fact]
    public async Task SmsVerify_ValidScene_200()
    {
        // 匿名游客帧（AllowAnonymous）：合法 scene → VerifyCodeAsync（Fake 恒 true）→ 200 verified
        AuthCenterEndpointHost.SmsVerification.Reset(); // 共享宿主——负向断言前置重置（V0.8.0）
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/sms/verify",
            new SmsVerifyRequest("13800138000", "123456", SmsScenes.Bind));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SmsVerifyResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Verified);
        Assert.True(AuthCenterEndpointHost.SmsVerification.SendCodeInvoked is false); // 独立校验不触发发送
    }

    [Fact]
    public async Task SmsVerify_InvalidScene_400()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/sms/verify",
            new SmsVerifyRequest("13800138000", "123456", "hack"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("INVALID_SCENE", body!.Code);
    }

    // ═══════════════════ 配置绑定（RoutePrefix/端点开关可配性——DomainHost 单例禁多宿主黑盒） ═══════════════════

    [Fact]
    public void ConfigBinding_EndpointOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TKWF:AuthCenter:Web:RoutePrefix"] = "/custom",
            ["TKWF:AuthCenter:Web:SmsLoginEndpointEnabled"] = "false",
            ["TKWF:AuthCenter:Web:ExternalLoginEndpointEnabled"] = "false",   // T5 新开关（替代 WechatLoginEndpointEnabled）
            ["TKWF:AuthCenter:Web:TokenVerifyEndpointEnabled"] = "false",   // V0.8.0 新开关
            ["TKWF:AuthCenter:Web:GrantsEndpointEnabled"] = "false",         // V0.8.0 新开关
            ["TKWF:AuthCenter:Web:SmsVerifyEndpointEnabled"] = "false"       // V0.8.0 新开关
        }).Build();
        var options = new AuthCenterEndpointOptions();
        config.GetSection("TKWF:AuthCenter:Web").Bind(options);

        Assert.Equal("/custom", options.RoutePrefix);
        Assert.False(options.SmsLoginEndpointEnabled);      // 配置覆盖
        Assert.False(options.ExternalLoginEndpointEnabled); // T5 配置覆盖
        Assert.False(options.TokenVerifyEndpointEnabled);   // V0.8.0 新开关配置覆盖
        Assert.False(options.GrantsEndpointEnabled);
        Assert.False(options.SmsVerifyEndpointEnabled);
        Assert.True(options.SmsSendCodeEndpointEnabled);    // 未配置保持默认
        Assert.True(options.TicketExchangeEndpointEnabled); // 未配置保持默认
    }

    [Fact]
    public void ConfigBinding_NewEndpointOptions_DefaultTrue()
    {
        // V0.8.0 3 新开关默认 true = 暴露面默认开放（领域 EnabledAuthTypes 未启用 Provider 时领域层 fail-closed 拒绝）
        var options = new AuthCenterEndpointOptions();
        Assert.True(options.TokenVerifyEndpointEnabled);
        Assert.True(options.GrantsEndpointEnabled);
        Assert.True(options.SmsVerifyEndpointEnabled);
        Assert.True(options.ExternalLoginEndpointEnabled);   // T5 外部 IdP 登录端点默认开放
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
