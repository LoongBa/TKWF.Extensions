using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Exceptions;
using Xunit;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// AuthCenterWebExtension 端点黑盒冒烟（V0.6.0 归层迭代 T4/T5）——共享宿主 <see cref="AuthCenterEndpointHost"/>
/// （模式 A 全链宿主：ConfigWebAppDomain + ConsumerHostInitializer + UseWebSession + AuthCenterWebExtension——
/// DomainHost Root 进程内单例，全项目仅 1 个 Web 宿主，JwtAuth 顺序冒烟共用；游客帧真实）。
/// <para>领域门面为 Fake（RegisterServices 替换守卫工厂——普通 Scoped，guest.Use 帧内解析）：聚焦端点行为
/// 路由映射 / 匿名帧 / 认证登出 / 异常→HTTP / 免认证。领域 fail-closed（EnabledAuthTypes）由 Domain 层
/// 测试锁定（AuthenticationProductionPathTests）。</para>
/// <para>B3b（MapGenerated 生成化）：错误响应为 RFC 9457 ProblemDetails 信封——失败断言读
/// <c>errorCode</c> 扩展字段（读原始 JSON，不用 ReadFromJsonAsync&lt;ErrorResponse&gt;——类型已删）。</para>
/// <para>RoutePrefix/端点开关的可配性：黑盒需独立宿主（DomainHost 单例不可）——改**配置绑定单测**
/// （IConfiguration → AuthCenterEndpointOptions 绑定链路）+ 配置分层断言（Web Options 不镜像领域配置）。</para>
/// </summary>
public sealed class AuthCenterWebExtensionTests
{
    private const string DefaultPrefix = "/api/auth";

    /// <summary>B3b：读 ProblemDetails 信封 <c>errorCode</c> 扩展字段（camelCase——source-gen 序列化扩展键直写；
    /// 系统字段 Type/Title/Status/Detail/Instance 为 PascalCase 属性名。Oracle1 核心断言 = errorCode 存活）。</summary>
    private static async Task<string> ReadErrorCodeAsync(HttpContent content)
    {
        using var doc = JsonDocument.Parse(await content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("Title", out _) || root.TryGetProperty("title", out _), "ProblemDetails 信封缺 Title");
        Assert.True(root.TryGetProperty("errorCode", out var code), "ProblemDetails 信封缺 errorCode");
        return code.GetString()!;
    }

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
        // B3b：门面抛类型化异常（DomainException SMS_CODE_MISMATCH——替代 LoginResult FailReason）
        // 中间件映射 400 + ProblemDetails.errorCode；末尾还原（共享 Fake——串行下防污染）
        AuthCenterEndpointHost.SmsLogin.Throw = new DomainException("SMS_CODE_MISMATCH") { ErrorCode = "SMS_CODE_MISMATCH" };
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/sms", new SmsLoginRequest("13800138000", "000000"));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal("SMS_CODE_MISMATCH", await ReadErrorCodeAsync(resp.Content));
        }
        finally
        {
            AuthCenterEndpointHost.SmsLogin.Throw = null;
        }
    }

    [Fact]
    public async Task SmsSendCode_ValidScene_200()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/sms/send-code", new SendSmsCodeRequest("13800138000", SmsScenes.Login));

        // B3b：生成端点对 Task（void）默认 200（原 hand-written minimal API 显式 Results.NoContent → 204）
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(AuthCenterEndpointHost.SmsVerification.SendCodeInvoked);
    }

    [Fact]
    public async Task SmsSendCode_InvalidScene_400()
    {
        AuthCenterEndpointHost.SmsVerification.Reset(); // 共享宿主——负向断言前置重置（V0.8.0）
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/sms/send-code", new SendSmsCodeRequest("13800138000", "hack"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("INVALID_SCENE", await ReadErrorCodeAsync(resp.Content));
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
        // fail-hard（P7→B3b）：Federation 未装配 → 门面捕获守卫转 ServiceNotConfiguredException（503）
        AuthCenterEndpointHost.ExternalLogin.GuardExceptionMessage = "领域架构守卫：AddConstructibleService 实现类（ExternalIdpLoginService）帧外解析";
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/external/wechat_oauth",
                new Dictionary<string, string?> { ["code"] = "wx-code", ["channel_id"] = "fake-mp" });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
            Assert.Equal("EXTERNAL_IDP_NOT_CONFIGURED", await ReadErrorCodeAsync(resp.Content));
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
        Assert.Equal("INVALID_ARGUMENT", await ReadErrorCodeAsync(resp.Content));
    }

    [Fact]
    public async Task ExternalLogin_FailReason_MapsTo400()
    {
        // B3b：门面抛类型化异常（DomainException EXTERNAL_AUTH_FAILED）→ 400 + ProblemDetails.errorCode
        AuthCenterEndpointHost.ExternalLogin.GuardExceptionMessage = null;
        AuthCenterEndpointHost.ExternalLogin.Throw = new DomainException("EXTERNAL_AUTH_FAILED") { ErrorCode = "EXTERNAL_AUTH_FAILED" };
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/external/qq_oauth",
                new Dictionary<string, string?> { ["code"] = "qq-code", ["channel_id"] = "qq-mp" });

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal("EXTERNAL_AUTH_FAILED", await ReadErrorCodeAsync(resp.Content));
        }
        finally
        {
            AuthCenterEndpointHost.ExternalLogin.Throw = null;
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
    public async Task Refresh_SnakeCaseRefreshToken_Binds()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        using var content = new StringContent(
            """{"refresh_token":"rt-2"}""",
            System.Text.Encoding.UTF8, "application/json");
        var resp = await client.PostAsync($"{DefaultPrefix}/refresh", content);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("rt-2", AuthCenterEndpointHost.Token.LastRefreshToken);
    }

    [Fact]
    public async Task TokenResponse_SerializesDocumentedSnakeCaseKeys()
    {
        // 文档契约响应 = {access_token, refresh_token, token_type, expires_in}（V0.6.0 起即如此约定，
        // 实现此前仅 camelCase）——V0.10.1 双发：snake_case 键 + camelCase 键并存，两类消费方零破坏
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/refresh", new RefreshRequest("rt-1"));
        var raw = await resp.Content.ReadAsStringAsync();

        Assert.Contains("\"access_token\":\"at-2\"", raw);
        Assert.Contains("\"accessToken\":\"at-2\"", raw);
        Assert.Contains("\"refresh_token\":\"rt-2\"", raw);
        Assert.Contains("\"refreshToken\":\"rt-2\"", raw);
        Assert.Contains("\"token_type\":\"Bearer\"", raw);
        Assert.Contains("\"tokenType\":\"Bearer\"", raw);
        Assert.Contains("\"expires_in\":7200", raw);
        Assert.Contains("\"expiresIn\":7200", raw);
    }

    [Fact]
    public async Task Logout_WithBearer_200()
    {
        // 已认证端点：共享宿主已注册 FakeTokenVerifier（Bearer 验签 → RestoreUser 替换已认证帧 +
        // 中间件写 Items[AuthCenter.TokenValidationResult]）→ 生成端点调 RevokeCurrentSessionAsync
        // （void——记录 RevokedJti 模拟 ambient 撤销；B3b：Task 默认 200，原 handler 显式 204）
        var client = AuthCenterEndpointHost.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "valid-token");

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/logout", new { });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
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

    [Fact]
    public async Task TicketExchange_SnakeCaseCodeVerifierAppId_Binds()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        using var content = new StringContent(
            """{"ticket":"t-3","code_verifier":"pkce-3","app_id":"app-3","state":"st-3"}""",
            System.Text.Encoding.UTF8, "application/json");
        var resp = await client.PostAsync($"{DefaultPrefix}/ticket/exchange", content);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(AuthCenterEndpointHost.Ticket.LastRequest);
        // B3b：snake_case 文档键经 [JsonPropertyName] 别名绑定 → Effective* 生效值读取（camelCase 直通同名键）
        Assert.Equal("pkce-3", AuthCenterEndpointHost.Ticket.LastRequest!.EffectiveCodeVerifier);
        Assert.Equal("app-3", AuthCenterEndpointHost.Ticket.LastRequest.EffectiveAppId);
        Assert.Equal("st-3", AuthCenterEndpointHost.Ticket.LastRequest.State);
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
        // B3b：/verify 生成端点挂 RequireAuthorization()（ASP.NET 授权层）；测试宿主 /api 前缀免认证排除
        // （HttpAuth.ExcludedPathPrefixes）→ 401 语义下沉门面 ambient 判定——Fake 设 AuthRequired 模拟
        // 无 Bearer → 门面抛 AuthenticationException("AUTH_REQUIRED") → 中间件 401（认证异常正确语义）
        AuthCenterEndpointHost.Token.VerifyRequiresAuth = true;
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();

            var resp = await client.GetAsync($"{DefaultPrefix}/verify");

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            AuthCenterEndpointHost.Token.VerifyRequiresAuth = false;
        }
    }

    [Fact]
    public async Task Grants_WithBearer_ReturnsList()
    {
        // /grants：已认证帧 + appId（B3b 查询键微差——生成器不透传 [FromQuery]，camelCase appId 文档化）→
        // FakeAuthGrantQueryService 返回 app-1 授权行 → GrantsResponse
        var client = AuthCenterEndpointHost.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "valid-token");

        var resp = await client.GetAsync($"{DefaultPrefix}/grants?appId=app-1");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<GrantsResponse>();
        Assert.NotNull(body);
        var grant = Assert.Single(body!.Grants);
        Assert.Equal("app-1", grant.AppId);
        Assert.Equal(new[] { "a", "b" }, grant.Scopes);         // 逗号串拆分（门面 GrantView 构造器）
        Assert.Equal(AuthGrantSources.Login, grant.Source);
        Assert.Equal(0, grant.Status);
        Assert.True(AuthCenterEndpointHost.Grants.Invoked);
        Assert.Equal("app-1", AuthCenterEndpointHost.Grants.LastAppId);   // B3b：appId 透传门面
    }

    [Fact]
    public async Task Grants_NoBearer_401()
    {
        // B3b：同 Verify——生成端点 RequireAuthorization 之外，测试宿主 /api 免认证排除 → 401 语义下沉门面
        // ambient 判定；Fake 设 AuthRequired 模拟无 Bearer → 抛 AUTH_REQUIRED（认证异常 → 401）
        // ⚠️ 请求须带 appId：生成 handler 的 string appId 非可空 query 参数——缺参 model 绑定先 400（未达门面）
        AuthCenterEndpointHost.Grants.Reset(); // 共享宿主——负向断言前置重置（V0.8.0）
        AuthCenterEndpointHost.Grants.AuthRequired = true;
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();

            var resp = await client.GetAsync($"{DefaultPrefix}/grants?appId=app-1");

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            Assert.False(AuthCenterEndpointHost.Grants.Invoked);   // 认证守卫先于查询——门面未执行查询
        }
        finally
        {
            AuthCenterEndpointHost.Grants.AuthRequired = false;
        }
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
        Assert.Equal("INVALID_SCENE", await ReadErrorCodeAsync(resp.Content));
    }

    // ═══════════════════ V0.10.0 票据 SPA 兑现（POST /login/ticket——独立路由，P2-1） ═══════════════════

    [Fact]
    public async Task TicketLogin_Success_ReturnsTokenResponse()
    {
        // 匿名游客帧 + 独立路由：body = {ticket, channel_id?} → 门面成功 → TokenResponse（官方形态）
        AuthCenterEndpointHost.TicketLogin.GuardExceptionMessage = null;
        AuthCenterEndpointHost.TicketLogin.RateLimitedMessage = null;
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/ticket",
            new ExternalTicketLoginRequest("ticket-32B-base64url-abcdefghijklmnop", "wechat-event-1"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.Equal("at-t", body!.AccessToken);
        Assert.Equal("Bearer", body.TokenType);
        Assert.True(AuthCenterEndpointHost.TicketLogin.Invoked);
        Assert.Equal("ticket-32B-base64url-abcdefghijklmnop", AuthCenterEndpointHost.TicketLogin.LastTicket);  // ticket 透传
        Assert.Equal("wechat-event-1", AuthCenterEndpointHost.TicketLogin.LastChannelId);                       // channel_id 透传
    }

    [Fact]
    public async Task TicketLogin_SnakeCaseChannelId_BindsChannelId()
    {
        AuthCenterEndpointHost.TicketLogin.GuardExceptionMessage = null;
        AuthCenterEndpointHost.TicketLogin.RateLimitedMessage = null;
        var client = AuthCenterEndpointHost.Server.CreateClient();

        using var content = new StringContent(
            """{"ticket":"ticket-32B-base64url-abcdefghijklmnop","channel_id":"wechat-event-1"}""",
            System.Text.Encoding.UTF8, "application/json");
        var resp = await client.PostAsync($"{DefaultPrefix}/login/ticket", content);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("wechat-event-1", AuthCenterEndpointHost.TicketLogin.LastChannelId);
    }

    [Fact]
    public async Task TicketLogin_MissingTicket_400()
    {
        var client = AuthCenterEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/ticket",
            new ExternalTicketLoginRequest("", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("INVALID_ARGUMENT", await ReadErrorCodeAsync(resp.Content));
    }

    [Fact]
    public async Task TicketLogin_TicketInvalid_400()
    {
        // B3b：门面抛类型化异常（DomainException TICKET_INVALID）→ 400 + ProblemDetails.errorCode
        AuthCenterEndpointHost.TicketLogin.Throw = new DomainException("TICKET_INVALID") { ErrorCode = "TICKET_INVALID" };
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/ticket",
                new ExternalTicketLoginRequest("ticket-expired-xxx", null));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal("TICKET_INVALID", await ReadErrorCodeAsync(resp.Content));
        }
        finally
        {
            AuthCenterEndpointHost.TicketLogin.Throw = null;
        }
    }

    [Fact]
    public async Task TicketLogin_RateLimited_429()
    {
        // 频控（P2-4→B3b）：RateLimitedException TICKET_RATE_LIMITED → 429 + ProblemDetails.errorCode
        AuthCenterEndpointHost.TicketLogin.RateLimitedMessage = "TICKET_RATE_LIMITED";
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/ticket",
                new ExternalTicketLoginRequest("ticket-32B-base64url-abcdefghijklmnop", null));

            Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
            Assert.Equal("TICKET_RATE_LIMITED", await ReadErrorCodeAsync(resp.Content));
        }
        finally
        {
            AuthCenterEndpointHost.TicketLogin.RateLimitedMessage = null;
        }
    }

    [Fact]
    public async Task TicketLogin_Guard_503()
    {
        // fail-hard→B3b：ISealedTicketService 未装配 → 门面捕获守卫转 ServiceNotConfiguredException（503）
        AuthCenterEndpointHost.TicketLogin.GuardExceptionMessage = "领域架构守卫：AddConstructibleService 实现类（ExternalTicketLoginService）帧外解析";
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/ticket",
                new ExternalTicketLoginRequest("ticket-32B-base64url-abcdefghijklmnop", null));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
            Assert.Equal("TICKET_SERVICE_NOT_CONFIGURED", await ReadErrorCodeAsync(resp.Content));
        }
        finally
        {
            AuthCenterEndpointHost.TicketLogin.GuardExceptionMessage = null;   // 共享宿主还原
        }
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
            ["TKWF:AuthCenter:Web:SmsVerifyEndpointEnabled"] = "false",       // V0.8.0 新开关
            ["TKWF:AuthCenter:Web:TicketLoginEndpointEnabled"] = "false"      // V0.10.0 新开关（票据 SPA 兑现）
        }).Build();
        var options = new AuthCenterEndpointOptions();
        config.GetSection("TKWF:AuthCenter:Web").Bind(options);

        Assert.Equal("/custom", options.RoutePrefix);
        Assert.False(options.SmsLoginEndpointEnabled);      // 配置覆盖
        Assert.False(options.ExternalLoginEndpointEnabled); // T5 配置覆盖
        Assert.False(options.TokenVerifyEndpointEnabled);   // V0.8.0 新开关配置覆盖
        Assert.False(options.GrantsEndpointEnabled);
        Assert.False(options.SmsVerifyEndpointEnabled);
        Assert.False(options.TicketLoginEndpointEnabled);   // V0.10.0 新开关配置覆盖
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
        Assert.True(options.TicketLoginEndpointEnabled);     // V0.10.0 票据兑现端点默认开放（暴露面，安全由领域频控承载）
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

    // ═══════════════════ B3b 新增：errorCode 存活断言（Oracle1 核心） ═══════════════════

    [Fact]
    public async Task ErrorResponse_ProblemDetailsEnvelope_PreservesErrorCode()
    {
        // Oracle1 核心断言：门面抛 DomainException SMS_CODE_MISMATCH → 中间件映射 400 ProblemDetails——
        // errorCode 扩展字段在信封内完整存活（实测：系统字段 PascalCase——Type/Title/Status/Detail/Instance，
        // 扩展字段 camelCase——errorCode/exception）
        AuthCenterEndpointHost.SmsLogin.Throw = new DomainException("SMS_CODE_MISMATCH") { ErrorCode = "SMS_CODE_MISMATCH" };
        try
        {
            var client = AuthCenterEndpointHost.Server.CreateClient();
            var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/login/sms", new SmsLoginRequest("13800138000", "999999"));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var raw = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            Assert.Equal("https://httpstatuses.io/400", root.GetProperty("Type").GetString());
            Assert.Equal(400, root.GetProperty("Status").GetInt32());
            Assert.Equal("SMS_CODE_MISMATCH", root.GetProperty("errorCode").GetString());
            Assert.True(root.TryGetProperty("Instance", out _));
        }
        finally
        {
            AuthCenterEndpointHost.SmsLogin.Throw = null;
        }
    }
}
