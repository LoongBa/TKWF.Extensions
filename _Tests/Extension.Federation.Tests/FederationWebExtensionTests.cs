using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Ticket;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.Federation;
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
    // 端点挂载组路径 = 框架 Module 前缀模型：basePath /api + [assembly: ExtensionModule] DefaultPrefix feberation
    // （Oracle C+E 2026-10-24——原 RoutePrefix 配置前缀归框架 Module 前缀模型）
    private const string DefaultPrefix = "/api/feberation";
    private const string DefaultPlatformId = "wechat";

    // ═══════════════════ 端点冒烟（共享宿主） ═══════════════════

    /// <summary>(a)+(d)：POST /feberation/{platformId}/login（无 channelId → 单通道降级）——body 参数透传
    /// SsoChannelAuthContext → Fake 通道 AuthenticateAsync 被调 → 200 SsoChannelAuthResult；
    /// handler ResolveChannelAsync 单通道活跃数==1 → 解析唯一通道 → LoginAsync(cfg.ChannelId)。</summary>
    [Fact]
    public async Task SsoLogin_DefaultChannel_ParamsPassed_ReturnsResult()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/{DefaultPlatformId}/login",
            new SsoLoginWebRequest(new Dictionary<string, string?> { ["code"] = "auth-code-1", ["state"] = "st-1" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SsoChannelAuthResult>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.Equal("ext-100", body.ExternalUserId);
        Assert.Equal(2, body.AuthLevel);
        Assert.True(FederationEndpointHost.Login.Invoked);                       // 单通道降级 → LoginAsync(cfg.ChannelId)
        Assert.Equal("wechat-mp", FederationEndpointHost.Login.LastChannelId);  // 解析出的唯一活跃通道
        Assert.True(FederationEndpointHost.Channel.Authenticated);               // 通道 AuthenticateAsync 被调
        Assert.Equal("auth-code-1", FederationEndpointHost.Channel.LastContext!.Parameters["code"]); // 参数透传
        Assert.Equal("st-1", FederationEndpointHost.Channel.LastContext.Parameters["state"]);
    }

    /// <summary>(d)：POST /feberation/{platformId}/login/{channelId}——route 精确选区 → LoginAsync(channelId) → 上下文透传 → 200。</summary>
    [Fact]
    public async Task SsoLogin_RouteChannel_ParamsPassed_ReturnsResult()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/{DefaultPlatformId}/login/wechat-mp",
            new SsoLoginWebRequest(new Dictionary<string, string?> { ["code"] = "auth-code-2" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SsoChannelAuthResult>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.True(FederationEndpointHost.Login.Invoked);                   // 有 channelId → LoginAsync
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

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/{DefaultPlatformId}/login/unknown-channel",
            new SsoLoginWebRequest(new Dictionary<string, string?> { ["code"] = "x" }));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("CHANNEL_NOT_FOUND", body!.Code);
        Assert.False(FederationEndpointHost.Channel.Authenticated);          // 选区失败不触达通道
    }

    /// <summary>B 守卫（Oracle 评审条件 1）：多通道（活跃数 >1）+ 省略 channelId → 400 CHANNEL_REQUIRED
    /// （防 GetDefaultAsync IsDefault→首项 非确定性静默选区）。</summary>
    [Fact]
    public async Task SsoLogin_MultiChannel_NoChannelId_400_ChannelRequired()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Registry.Channels = new List<ChannelConfig>
        {
            new() { ChannelId = "wechat-mp", PlatformType = "wechat", IsEnabled = true },
            new() { ChannelId = "wechat-mp-2", PlatformType = "wechat", IsEnabled = true },
        };
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/{DefaultPlatformId}/login",
            new SsoLoginWebRequest(new Dictionary<string, string?> { ["code"] = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("CHANNEL_REQUIRED", body!.Code);
        Assert.False(FederationEndpointHost.Login.Invoked);                   // 守卫拦截不触达门面
    }

    /// <summary>平台段 Ordinal 校验（Oracle 评审条件 2）：channelId 存在但 PlatformType != 路由 platformId → 404
    /// CHANNEL_NOT_FOUND（统一防枚举——不泄露"通道存在但平台写错" vs "通道不存在"）。</summary>
    [Fact]
    public async Task SsoLogin_PlatformMismatch_404_ChannelNotFound()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/dingtalk/login/wechat-mp",
            new SsoLoginWebRequest(new Dictionary<string, string?> { ["code"] = "x" }));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("CHANNEL_NOT_FOUND", body!.Code);
        Assert.False(FederationEndpointHost.Login.Invoked);                   // 平台错配不触达门面
    }

    /// <summary>通道存在但 IsEnabled=false（显式 channelId）→ 403 CHANNEL_DISABLED（MapFailReason 显式映射——
    /// Oracle 复查 Issue 1：原缺 case 落入默认 400，与文档约定 403 不符）。</summary>
    [Fact]
    public async Task SsoLogin_DisabledChannel_403_ChannelDisabled()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Registry.Channels = new List<ChannelConfig>
        {
            new() { ChannelId = "wechat-mp", PlatformType = "wechat", IsEnabled = false },   // 禁用通道
        };
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsJsonAsync($"{DefaultPrefix}/{DefaultPlatformId}/login/wechat-mp",
            new SsoLoginWebRequest(new Dictionary<string, string?> { ["code"] = "x" }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("CHANNEL_DISABLED", body!.Code);
        Assert.False(FederationEndpointHost.Login.Invoked);                   // 禁用通道不触达门面
    }

    /// <summary>(c)：GET /feberation/jwks——信任内核 JWKS 公钥分发（匿名公开，跨平台根级）→ 200 + JSON 含 kid。</summary>
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

    // ═══════════════════ 平台事件推送（缺陷修复 2026-10-11） ═══════════════════

    /// <summary>event 端点（POST /feberation/{platformId}/event/{channelId}）——channelType 按平台段推导
    /// <c>{PlatformType}_event</c> 精确匹配 *_event 模板（缺陷修复：原 null 分支 IsOAuthChannel 白名单仅
    /// *_oauth/*_oidc，event 通道不可达）——Fake 工厂收到 channelType="wechat_event"。</summary>
    [Fact]
    public async Task Event_ChannelTypeDerivedFromPlatformId()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("wechat-mp", FederationEndpointHost.Factory.LastChannelId);
        Assert.Equal("wechat_event", FederationEndpointHost.Factory.LastChannelType);   // 平台段推导修复断言
        Assert.True(FederationEndpointHost.Channel.Authenticated);
        Assert.Equal("""{"EventType":"subscribe"}""", FederationEndpointHost.Channel.LastContext!.Parameters["__raw"]);
    }

    /// <summary>event 端点平台段错配 → 404（统一防枚举，对齐平台校验语义）。</summary>
    [Fact]
    public async Task Event_PlatformMismatch_404()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/dingtalk/event/wechat-mp",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.False(FederationEndpointHost.Factory.CreateInvoked);
    }

    /// <summary>event 端点 S3（D03 1.8）——通道停用（显式 channelId + IsEnabled=false）→ 200 空回复
    /// （微信对非 200 重试——停用通道按 200 空处理防重试风暴；提示文案 XML 信封归 handler 层渲染）。</summary>
    [Fact]
    public async Task Event_DisabledChannel_200Empty()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Registry.Channels = new List<ChannelConfig>
        {
            new() { ChannelId = "wechat-mp", PlatformType = "wechat", IsEnabled = false },
        };
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/xml", resp.Content.Headers.ContentType?.MediaType);
        Assert.Equal(string.Empty, await resp.Content.ReadAsStringAsync());
        Assert.False(FederationEndpointHost.Factory.CreateInvoked);      // 停用通道不触达通道工厂
    }

    /// <summary>event 端点 S2（D03 1.8）——未知通道默认 404 静默（UnknownChannelReplyEnabled=false 防探测）。</summary>
    [Fact]
    public async Task Event_UnknownChannel_Default_404()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/ghost",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.False(FederationEndpointHost.Factory.CreateInvoked);
    }

    /// <summary>event 端点 S2（D03 1.8）——UnknownChannelReplyEnabled=true → 未知通道 200 空回复
    /// （微信不重试；探测者不可区分「通道不存在」与「无回复」）。</summary>
    [Fact]
    public async Task Event_UnknownChannel_SilentEnabled_200Empty()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.EventReplyOptions.UnknownChannelReplyEnabled = true;
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/ghost",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/xml", resp.Content.Headers.ContentType?.MediaType);
        Assert.Equal(string.Empty, await resp.Content.ReadAsStringAsync());
        Assert.False(FederationEndpointHost.Factory.CreateInvoked);
    }

    /// <summary>event 端点 S1（D03 1.8）——验签失败（信任根一票否决，硬线不可配置）→ 400 AUTHENTICATION_FAILED
    /// + 不触达编排（Factory 已构造但 Orchestrator 不 invoked——编排不短路通道实例构造）。</summary>
    [Fact]
    public async Task Event_AuthFailed_400_AuthenticationFailed_PerSchemeHardwire()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Channel.Result = new SsoChannelAuthResult(false, null, "AUTHENTICATION_FAILED", 2);
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("AUTHENTICATION_FAILED", body!.Code);
        Assert.Equal(0, FederationEndpointHost.Orchestrator.Invoked);   // 验签失败 → 编排不 invoked（信任根一票否决）
    }

    /// <summary>event 端点无通道段（单通道降级）——唯一活跃通道解析 → 平台段推导 + 构造。</summary>
    [Fact]
    public async Task Event_NoChannelId_SingleChannel_DerivesAndConstructs()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("wechat-mp", FederationEndpointHost.Factory.LastChannelId);
        Assert.Equal("wechat_event", FederationEndpointHost.Factory.LastChannelType);
    }

    // ═══════════════════ 事件链（外部身份联邦 E3——query 透传 + XML 回复 + handler + uid 只读） ═══════════════════

    /// <summary>event 端点 query 透传（现有缺陷修复——微信验签参数在 URL query）+ 微信 XML 回复：
    /// POST event 端点带 query（msg_signature/timestamp/nonce）→ 验签成功 → 编排 → handler 回复 →
    /// 200 + ContentType text/xml；query 参数在 Fake 入站上下文 Parameters。</summary>
    [Fact]
    public async Task Event_QueryParamsPassed_XmlReply()
    {
        FederationEndpointHost.Reset();
        // I2 语义层：Fake 通道产 Parsed(subscribe) → 编排走 handler——回复由 handler 产出（通道不再自回 XML）
        FederationEndpointHost.EventHandler1.Result =
            new EventReplyResult("<xml><MsgType><![CDATA[text]]></MsgType><Content><![CDATA[ok]]></Content></xml>", "text/xml");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync(
            $"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp?msg_signature=sig-1&timestamp=123&nonce=nc-1",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/xml", resp.Content.Headers.ContentType?.MediaType);   // 微信要求 XML 回复
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("<MsgType><![CDATA[text]]></MsgType>", body);

        // query 透传（微信验签参数在 URL query——现有缺陷修复断言）
        Assert.NotNull(FederationEndpointHost.Channel.LastEventContext);
        Assert.Equal("sig-1", FederationEndpointHost.Channel.LastEventContext!.Parameters["msg_signature"]);
        Assert.Equal("123", FederationEndpointHost.Channel.LastEventContext.Parameters["timestamp"]);
        Assert.Equal("nc-1", FederationEndpointHost.Channel.LastEventContext.Parameters["nonce"]);
        Assert.Equal("""{"EventType":"subscribe"}""", FederationEndpointHost.Channel.LastEventContext.Parameters["__raw"]);
        Assert.Equal("wechat-mp", FederationEndpointHost.Channel.LastEventContext.Parameters["channel_id"]);
    }

    // P1 安全修复（审核 F1，2026-10-26）：base_url 服务端可信填充——最后写覆盖 query，防攻击者
    // query 注入 ?base_url=https://evil.com 劫持免登票据落地域（对齐 HandleAuthorizeStart 防伪造回调域语义）
    /// <summary>event 端点 base_url 服务端注入不被 query 覆盖（P1 安全修复回归锁）：POST 带恶意
    /// <c>?base_url=...evil...</c> → LastEventContext.Parameters["base_url"] 仍为服务端 scheme://host
    /// （非 query 值）——防伪造回调域（攻击者可劫持图文 {ticket} 链接 → 票据外泄冒充受害者）。</summary>
    [Fact]
    public async Task Event_BaseUrl_ServerTrusted_NotOverridableByQuery()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.EventHandler1.Result =
            new EventReplyResult("<xml><MsgType><![CDATA[text]]></MsgType><Content><![CDATA[ok]]></Content></xml>", "text/xml");
        var client = FederationEndpointHost.Server.CreateClient();

        // 恶意 query 注入 base_url（攻击者企图劫持票据链接落地域——服务端必须权威覆盖）
        var resp = await client.PostAsync(
            $"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp?base_url=https://evil.example.com&msg_signature=sig-1&timestamp=123&nonce=nc-1",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(FederationEndpointHost.Channel.LastEventContext);
        // P1 修复核心断言：base_url = 服务端 scheme://host（TestServer 默认 http://localhost），非 query 恶意值
        var injected = FederationEndpointHost.Channel.LastEventContext!.Parameters["base_url"];
        Assert.False(string.IsNullOrWhiteSpace(injected));
        Assert.DoesNotContain("evil.example.com", injected, System.StringComparison.Ordinal);
        Assert.StartsWith("http://", injected, System.StringComparison.Ordinal);
    }

    /// <summary>URL 验证握手（echostr——微信回调 URL 验证契约）：GET query 含 echostr 且验签通过 → 原样回显（text/plain）。
    /// 双模式验签（VerifyUrlAsync——安全 msg_signature 4 参 + AES 解密 echostr / 明文 signature 3 参）。</summary>
    [Fact]
    public async Task Event_Echostr_UrlVerification_ReturnsEcho()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync(
            $"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp?msg_signature=sig-1&timestamp=123&nonce=nc-1&echostr=echo-abc");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal("echo-abc", body);                                          // 验签通过 → 原样回显
        Assert.True(FederationEndpointHost.Channel.UrlVerified);                // VerifyUrlAsync 已执行
    }

    /// <summary>handler first-non-null-wins（评审 F8）：Fake 通道平台回复 null → 编排委托 IEventReplyHandler 集合
    /// 按 ChannelType 匹配 → 首个非 null 生效（handler-1）→ 200 回复；handler-2 不触达（短路）。</summary>
    [Fact]
    public async Task Event_HandlerFirstNonNullWins()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.EventHandler1.Result = new EventReplyResult("<xml><Content><![CDATA[from-handler-1]]></Content></xml>", "text/xml");
        FederationEndpointHost.EventHandler2.Result = new EventReplyResult("<xml><Content><![CDATA[from-handler-2]]></Content></xml>", "text/xml");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/xml", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("from-handler-1", body);                                 // 首个非 null 生效
        Assert.DoesNotContain("from-handler-2", body);
        Assert.Equal(1, FederationEndpointHost.EventHandler1.Invoked);           // handler-1 被调
        Assert.Equal(0, FederationEndpointHost.EventHandler2.Invoked);           // first-non-null-wins 短路
    }

    /// <summary>uid 只读查询（评审 F7）：编排经 IExternalIdentityService.GetByExternalIdAsync 查 uid（不建号——
    /// LinkAsync 不被调）→ 富化 Parameters["uid"] 供 handler 签票据。</summary>
    [Fact]
    public async Task Event_UidReadOnlyLookup_EnrichedIntoHandlerContext()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.ChannelMap.Map = new ExternalIdentityDto("u-100", "wechat-mp", "ext-100", DateTime.UtcNow);
        FederationEndpointHost.EventHandler1.Result = new EventReplyResult("<xml><Content><![CDATA[login-link]]></Content></xml>", "text/xml");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // uid 只读查询（GetByExternalIdAsync 被调、LinkAsync 不被调）
        Assert.Equal(1, FederationEndpointHost.ChannelMap.GetInvoked);
        Assert.Equal("wechat-mp", FederationEndpointHost.ChannelMap.LastChannelId);
        Assert.Equal("ext-100", FederationEndpointHost.ChannelMap.LastExternalUserId);  // 验签富化的 external_user_id
        Assert.Equal(0, FederationEndpointHost.ChannelMap.LinkInvoked);                 // 只读不建号

        // uid 富化进 handler 派发（供 handler 签 ISealedTicketService<FederatedIdentity> 票据）
        Assert.NotNull(FederationEndpointHost.EventHandler1.LastDispatch);
        Assert.Equal("u-100", FederationEndpointHost.EventHandler1.LastDispatch!.UId);
    }

    // ═══════════════════ 子应用消费方委托端点冒烟（ISsoSubAppBridge——2026-10-11） ═══════════════════

    /// <summary>(1)：POST /feberation/authorize/start——匿名发起外部认证成功 → 200 {authorize_url, state}；
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

    /// <summary>(3)：GET /feberation/{platformId}/oauth/{channelId}/callback——OAuth IdP 302 重定向落点（<b>GET</b> 方法）
    /// 成功 → 302 Location 含 code + state（跳回子应用）；query 参数透传 Fake。</summary>
    [Fact]
    public async Task OAuthCallback_Get_302_LocationContainsCodeAndState()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Bridge.CallbackResult =
            new(true, "https://app.example.com/cb?code=ac-1&state=sub-state-1", "sub-state-1", null);
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync(
            $"{DefaultPrefix}/{DefaultPlatformId}/oauth/wechat-mp/callback?code=wechat-code-1&state=ticket-1");

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

    /// <summary>(3b)：GET /feberation/{platformId}/oauth/callback（无通道段，单通道降级）——唯一活跃通道解析 →
    /// CompleteOAuthAsync(cfg.ChannelId)——无前缀降级形态（文档漂移补实）。</summary>
    [Fact]
    public async Task OAuthCallback_NoChannelId_SingleChannel_ResolvesDefault()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Bridge.CallbackResult =
            new(true, "https://app.example.com/cb?code=ac-1&state=sub-state-1", "sub-state-1", null);
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync(
            $"{DefaultPrefix}/{DefaultPlatformId}/oauth/callback?code=wechat-code-1&state=ticket-1");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("wechat-mp", FederationEndpointHost.Bridge.LastCallbackChannelId);   // 单通道降级解析
        Assert.Equal("wechat-code-1", FederationEndpointHost.Bridge.LastCallbackParams!["code"]);
    }

    /// <summary>(3c)：OAuth 回调多通道 + 无通道段 → 400 CHANNEL_REQUIRED（B 守卫）。</summary>
    [Fact]
    public async Task OAuthCallback_MultiChannel_NoChannelId_400_ChannelRequired()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Registry.Channels = new List<ChannelConfig>
        {
            new() { ChannelId = "wechat-mp", PlatformType = "wechat", IsEnabled = true },
            new() { ChannelId = "wechat-mp-2", PlatformType = "wechat", IsEnabled = true },
        };
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync(
            $"{DefaultPrefix}/{DefaultPlatformId}/oauth/callback?code=wechat-code-1&state=ticket-1");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("CHANNEL_REQUIRED", body!.Code);
        Assert.Null(FederationEndpointHost.Bridge.LastCallbackChannelId);      // 守卫拦截不触达门面
    }

    /// <summary>(4a)：POST /feberation/trust/issue——已认证帧（测试哨兵中间件 X-Test-Auth-User 写入 Items["DomainUser"]）
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

    /// <summary>(4b)：POST /feberation/trust/issue——未认证（无已认证帧）→ 401 UNAUTHENTICATED（不触达门面）。</summary>
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

    /// <summary>(5a)：POST /feberation/identity/claim——子应用兑现成功 → 200 {mode, channel_id, external_user_id}。</summary>
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

    /// <summary>W3 双桶解析（2026-10-16）：通道特定绑定优先——EventHandler1 绑定 wechat-mp（IChannelSpecificEventReplyHandler）
    /// 精确命中 → 回复（handler1）；同 ChannelType 的 handler2（类型桶）不触达。</summary>
    [Fact]
    public async Task Event_BoundHandler_PriorityOverTypeBucket()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.EventHandler1.BindChannelId = "wechat-mp";   // 绑定桶
        FederationEndpointHost.EventHandler1.Result = new EventReplyResult("<xml><Content><![CDATA[from-bound]]></Content></xml>", "text/xml");
        FederationEndpointHost.EventHandler2.Result = new EventReplyResult("<xml><Content><![CDATA[from-type]]></Content></xml>", "text/xml");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("from-bound", body);                    // 绑定桶优先
        Assert.DoesNotContain("from-type", body);
        Assert.Equal(1, FederationEndpointHost.EventHandler1.Invoked);   // 绑定 handler 被调
        Assert.Equal(0, FederationEndpointHost.EventHandler2.Invoked);   // 类型桶不触达（绑定命中短路）
    }

    /// <summary>W3 双桶解析：绑定未命中（绑定 channelId ≠ 事件通道）且绑定 handler 的 ChannelType 不参与
    /// （dingtalk_event vs wechat_event）→ 回退类型桶默认（handler2 共用）。</summary>
    [Fact]
    public async Task Event_BoundHandler_MissingChannel_FallsBackToTypeBucket()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.EventHandler1.BindChannelId = "other-channel";   // 绑定不匹配
        FederationEndpointHost.EventHandler1.ChannelType = "dingtalk_event";   // 类型桶亦不参与（纯绑定 handler）
        FederationEndpointHost.EventHandler1.Result = new EventReplyResult("<xml><Content><![CDATA[from-bound]]></Content></xml>", "text/xml");
        FederationEndpointHost.EventHandler2.Result = new EventReplyResult("<xml><Content><![CDATA[from-type]]></Content></xml>", "text/xml");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("from-type", body);                     // 回退类型桶
        Assert.DoesNotContain("from-bound", body);
        Assert.Equal(0, FederationEndpointHost.EventHandler1.Invoked);   // 绑定不匹配 + 类型不参与 → 未触达
        Assert.Equal(1, FederationEndpointHost.EventHandler2.Invoked);   // 类型桶兜底
    }

    /// <summary>W3 双桶解析：默认共用——无绑定声明（BindChannelId=null）→ 全走类型桶（现状语义，默认所有 channelId 共用）。</summary>
    [Fact]
    public async Task Event_NoBinding_DefaultTypeBucketShared()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.EventHandler1.BindChannelId = null;       // 默认共用（类型桶）
        FederationEndpointHost.EventHandler1.Result = new EventReplyResult("<xml><Content><![CDATA[from-shared]]></Content></xml>", "text/xml");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.PostAsync($"{DefaultPrefix}/{DefaultPlatformId}/event/wechat-mp",
            new StringContent("""{"EventType":"subscribe"}""", System.Text.Encoding.UTF8, "application/xml"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("from-shared", body);                  // 类型桶命中（默认共用）
        Assert.Equal(1, FederationEndpointHost.EventHandler1.Invoked);
    }

    // ═══════════════════ 测试端点（ticket 闭环——2026-10-16 路径/命名专题改造） ═══════════════════

    /// <summary>TestTicket：GET /feberation/{platformId}/test/{channelId}/{ticket——平台段 + 通道段选区通过 →
    /// RedeemAsync(ticket, context:null) → 200 HTML 展示页（微信内置浏览器渲染——裸 JSON 白屏，2026-10-26 修复；
    /// channel_id 取自签入载荷 FederatedIdentity.ChannelId——用户裁定 3：单通道也必须能取出、与选区分支无关）。</summary>
    [Fact]
    public async Task TestTicket_ByPlatformAndChannel_ReturnsPayload()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Ticket.RedeemResult = FederatedTicketPayloadBuilder.Build(
            provider: "wechat", protocolChannelType: "wechat_event", channelId: "wechat-mp",
            externalUserId: "ext-100", platformUid: "u-100", assurance: "event");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync($"{DefaultPrefix}/wechat/test/wechat-mp/ticket-1");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("text/html", resp.Content.Headers.ContentType?.ToString());
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("票据兑验成功", body);
        Assert.Contains("u-100", body);                 // uid（签入载荷 PlatformUid）
        Assert.Contains("wechat-mp", body);             // channel_id（签入载荷 ChannelId——选区无关）
        Assert.Contains("ext-100", body);               // external_user_id
        Assert.Contains("wechat_event", body);          // protocol_type（协议类型——测试端点展示字段）
        Assert.Equal("ticket-1", FederationEndpointHost.Ticket.LastTicket);   // RedeemAsync 收到 ticket
        Assert.Null(FederationEndpointHost.Ticket.LastContext);               // 测试端点 Redeem context=null（可转让语义）
    }

    /// <summary>TestTicket HTML 转义回归锁（2026-10-26 审核 P2）：FederatedIdentity 载荷字段含 HTML 元字符
    /// （external_user_id 来自微信回调外部输入——恶意 <c>&lt;script&gt;</c>）→ HTML 展示页必须转义
    /// （<c>&amp;lt;script&amp;gt;</c>），原始 <c>&lt;script&gt;</c> 不得出现在响应（XSS 面关闭）。</summary>
    [Fact]
    public async Task TestTicket_HtmlEscapesExternalInput()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Ticket.RedeemResult = FederatedTicketPayloadBuilder.Build(
            provider: "wechat", protocolChannelType: "wechat_event", channelId: "wechat-mp",
            externalUserId: "<script>alert(1)</script>", platformUid: "u-100", assurance: "event");
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync($"{DefaultPrefix}/wechat/test/wechat-mp/ticket-1");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;", body);        // 转义后实体（标签闭合符转义）
        Assert.DoesNotContain("<script>", body);         // 原始标签不得出现（XSS 面关闭）
        // 注：alert(1) 为脚本内容非元字符 → HtmlEncode 不转义，作为纯文本保留（不在 <script> 标签内，浏览器不执行）——安全
    }

    /// <summary>TestTicket 平台段错配（channelId 存在但 PlatformType != 路由 platformId）→ 404 CHANNEL_NOT_FOUND
    /// （统一防枚举——对齐平台集成面校验语义，不触达 Redeem）。</summary>
    [Fact]
    public async Task TestTicket_PlatformMismatch_404()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync($"{DefaultPrefix}/dingtalk/test/wechat-mp/ticket-1");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("CHANNEL_NOT_FOUND", body!.Code);
        Assert.Null(FederationEndpointHost.Ticket.LastTicket);                 // 平台错配不触达 Redeem
    }

    /// <summary>TestTicket 未知通道 → 404 CHANNEL_NOT_FOUND（选区失败映射）。</summary>
    [Fact]
    public async Task TestTicket_UnknownChannel_404()
    {
        FederationEndpointHost.Reset();
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync($"{DefaultPrefix}/wechat/test/unknown-channel/ticket-1");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("CHANNEL_NOT_FOUND", body!.Code);
        Assert.Null(FederationEndpointHost.Ticket.LastTicket);
    }

    /// <summary>TestTicket 无效 ticket（Redeem 返回 null——未发现/过期/已用/核销人不符）→ 404 TICKET_NOT_FOUND（契约语义）。</summary>
    [Fact]
    public async Task TestTicket_UnknownTicket_404()
    {
        FederationEndpointHost.Reset();                                        // RedeemResult 默认 null
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync($"{DefaultPrefix}/wechat/test/wechat-mp/unknown-ticket");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("TICKET_NOT_FOUND", body!.Code);
        Assert.Equal("unknown-ticket", FederationEndpointHost.Ticket.LastTicket); // Redeem 被调（选区通过）
    }

    /// <summary>TestTicket 通道禁用（显式 channelId + IsEnabled=false）→ 403 CHANNEL_DISABLED（MapFailReason 显式映射）。</summary>
    [Fact]
    public async Task TestTicket_DisabledChannel_403()
    {
        FederationEndpointHost.Reset();
        FederationEndpointHost.Registry.Channels = new List<ChannelConfig>
        {
            new() { ChannelId = "wechat-mp", PlatformType = "wechat", IsEnabled = false },   // 禁用通道
        };
        var client = FederationEndpointHost.Server.CreateClient();

        var resp = await client.GetAsync($"{DefaultPrefix}/wechat/test/wechat-mp/ticket-1");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("CHANNEL_DISABLED", body!.Code);
        Assert.Null(FederationEndpointHost.Ticket.LastTicket);                 // 禁用不触达 Redeem
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
