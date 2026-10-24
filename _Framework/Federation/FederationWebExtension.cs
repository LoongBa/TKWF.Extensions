using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Ticket;
using TKW.Framework.Domain.Web.Extensions;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 联邦互联 Web 装配扩展（三层架构重构 T3 装配面——Federation 对外连接层 <c>/{prefix}/*</c> 端点；v4.10.45
/// Web 装配钩子 ADR87/D22/G18）——自描述 Web 装配需求，消费方 <c>UseWebExtensions</c> 一次声明。
/// <para>锚点：默认 <see cref="MiddlewareAnchor.AfterRouting"/>（纯端点装配，无中间件——<see cref="ConfigureMiddleware"/>
/// 空实现）。端点均为匿名（<c>AllowAnonymous</c>）——对外连接面（平台回调/公共 JWKS），
/// 不经 HttpAuthentication 认证（<b>v4.10.76 起框架自动排除</b>：<see cref="HttpAuthenticationMiddleware{TUserInfo}"/>
/// 锚点后移 AfterRouting 读端点 <c>IAllowAnonymous</c> 元数据——本扩展所有端点
/// <c>AllowAnonymous</c>（除 trust/issue）自动免 HttpAuth 认证，<b>消费方零配置</b>，不再需要
/// <c>HttpAuth.ExcludedPathPrefixes.Add(...)</c>；详见 <c>docs/HttpAuth匿名端点排除机制-开发方案.md</c>）。</para>
/// <para>领域自治（G18 §3）：<see cref="ISsoLogin"/>/<see cref="ISsoChannelFactory"/> 等领域门面留 Domain 钩子
/// <see cref="FederationExtensionInitializer{TUserInfo}"/> 注册（AddConstructibleService 守卫工厂）——本 Web
/// 钩子仅宿主注册（端点映射 + 端点 Options），handler 经游客帧 <c>guest.Use&lt;门面&gt;()</c> 帧内解析。</para>
/// <para>配置分层（AGENTS §8）：领域决策在 <see cref="FederationChannelRegistryOptions"/>（TKWF:Federation:ChannelRegistry——
/// 通道启用/加密密钥路径）/ TrustCenter（TKWF:TrustCenter——令牌签发/密钥路径）不动；暴露面（RoutePrefix +
/// 端点开关）在 <see cref="FederationEndpointOptions"/>（TKWF:Federation:Web——不镜像领域配置）。</para>
/// <para><b>端点（平台集成面 6 + 信任/委托面 4，对外路由命名空间 2026-10-11——/sso → /feberation + 平台段）</b>：
/// <c>POST {prefix}/{platformId}/login[/{channelId}]</c>（平台段 Ordinal 校验 + B 守卫；无 channelId 单通道降级 → 精确选区）——
/// body/route 传参数（code 等）→ <c>SsoChannelAuthContext</c> → <see cref="SsoChannelAuthResult"/> JSON；
/// <c>GET {prefix}/jwks</c>——JWKS 公钥分发（信任内核职责，经 <see cref="IToken2Service.GetJwksJson"/>；跨平台根级）；
/// <c>POST {prefix}/{platformId}/event[/{channelId}]</c>——平台事件推送接收（端点只中转：原始 body 包参数字典
/// <c>{"__raw": ..., "channel_id": ...}</c> → <see cref="ISsoChannelFactory.CreateAsync"/> 构造（channelType 按平台段推导
/// <c>{PlatformType}_event</c>——缺陷修复）→ <c>channel.AuthenticateAsync</c>——事件验签由平台库通道实现内部完成，一票否决）；
/// <b>子应用消费方委托（<see cref="ISsoSubAppBridge"/> 编排）</b>：<c>POST {prefix}/authorize/start</c>（匿名——
/// 发起外部认证，redirect 白名单 + state 票据 + 通道授权 URL 构造）、<c>GET {prefix}/{platformId}/oauth[/{channelId}]/callback</c>
/// （匿名——平台 IdP 302 回调落点，验 state → 认证 → 签 accesscode → 302 跳回子应用；<b>须 GET</b>——IdP 重定向语义）、
/// <c>POST {prefix}/trust/issue</c>（<b>已认证</b> Bearer token1——direct 模式 Platform 登录态签发；须 AuthCenterWebExtension
/// 同装配）、<c>POST {prefix}/identity/claim</c>（匿名 + 子应用 credential——兑现 accesscode 原子取回，统一 401 防枚举）。</para>
/// <para>⚠️ 装配约束：须与 <c>UseWebSession</c> 同装配（ContextExtraction 阶段 2 写游客 DomainUser——
/// 匿名端点 handler 经游客帧 <c>guest.Use&lt;门面&gt;()</c> 调用守卫工厂门面；未装配 → 抛守卫 = 正确 fail
/// 非静默降级）。<c>trust/issue</c> 端点另须 <c>AuthCenterWebExtension</c> 同装配（已认证帧写入
/// <c>Items["DomainUser"]</c>——Platform 档位④自然满足）。</para>
/// <para><b>authorize 端点说明（ADR-Federation-子应用委托授权URL构造契约化，2026-10-11 反转）</b>：既有
/// "契约无 BuildAuthorizeUrl——不臆造契约，授权链接构造归平台库/消费方"（N3 P1-3）决策被有意推翻——
/// 多子应用统一协议的 authorize/start 端点须通道实现构造授权 URL（<see cref="ISsoChannel.BuildAuthorizeUrlAsync"/>
/// DIM 默认 null，非破坏）；state 归本层（签名票据，AES-GCM Federation 密钥——Oracle 条件 3）。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public sealed class FederationWebExtension<TUserInfo> : IWebExtension
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>端点 Options 编程式配置（可选）——后于配置节 <c>TKWF:Federation:Web</c> 应用。</summary>
    public Action<FederationEndpointOptions>? ConfigureOptions { get; set; }

    /// <summary>宿主注册（Build 前立即执行；configure 委托先于本方法——实例属性已就绪，零延迟闭包）。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // 仅宿主注册：端点 Options（领域门面注册归 FederationExtensionInitializer——零重复）
        services.AddOptions<FederationEndpointOptions>();
        if (ConfigureOptions is not null)
            services.Configure(ConfigureOptions);
    }

    /// <summary>中间件锚点——纯端点装配无中间件；声明 AfterRouting（UseRouting 之后端点映射前，
    /// 对齐 NotificationsHub/HealthCheck 纯端点 WebExtension 先例），仅文档语义（空桶零副作用）。</summary>
    public MiddlewareAnchor MiddlewareAnchor => MiddlewareAnchor.AfterRouting;

    /// <summary>中间件挂载——纯端点装配，无中间件。</summary>
    public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options) { }

    /// <summary>协议适配端点声明（Oracle C+E 2026-10-24——ConfigureEndpoints 删除，F13）——对外连接层
    /// <c>/{prefix}/*</c> 端点（平台集成面 6 端点 + 信任/委托面 4 端点，对外路由命名空间 2026-10-11——
    /// /sso → /feberation 语义化 + 二级平台段），路径/开关经 <see cref="FederationEndpointOptions"/> 可配置。
    /// 经窄接口 <see cref="IProtocolEndpointBuilder"/> 声明（路由模板为数据、可配动词/授权；前缀拼接归框架
    /// Module 前缀模型——R2-3 无 builder 门禁保持，扩展不见 IEndpointRouteBuilder/RouteGroupBuilder）；
    /// 端点开关读 <see cref="FederationEndpointOptions"/>（经 <see cref="IProtocolEndpointBuilder.ServiceProvider"/> 解析）。</summary>
    public void DeclareProtocolEndpoints(IProtocolEndpointBuilder builder)
    {
        var endpointOptions = ResolveOptions(builder.ServiceProvider);

        // ── 平台集成面（二级 = 平台族 PlatformType，三级 = 端点组，四级 = 通道实例；平台段 Ordinal 校验防跨平台错配） ──
        if (endpointOptions.LoginEndpointEnabled)
        {
            builder.Map("/{platformId}/login", "POST").WithHandler(HandleSsoLogin);
            builder.Map("/{platformId}/login/{channelId}", "POST").WithHandler(HandleSsoLogin);
        }
        if (endpointOptions.EventEndpointEnabled)
        {
            builder.Map("/{platformId}/event", "POST").WithHandler(HandleEvent);
            builder.Map("/{platformId}/event/{channelId}", "POST").WithHandler(HandleEvent);
            // 微信 URL 验证握手（GET——微信后台服务器配置验证契约：signature/msg_signature+timestamp+nonce+echostr）
            builder.Map("/{platformId}/event", "GET").WithHandler(HandleEvent);
            builder.Map("/{platformId}/event/{channelId}", "GET").WithHandler(HandleEvent);
        }
        if (endpointOptions.OauthCallbackEndpointEnabled)
        {
            builder.Map("/{platformId}/oauth/callback", "GET").WithHandler(HandleOAuthCallback);
            builder.Map("/{platformId}/oauth/{channelId}/callback", "GET").WithHandler(HandleOAuthCallback);
        }

        // ── 信任/委托面（跨平台，根级——不挂平台段） ──
        if (endpointOptions.JwksEndpointEnabled)
            builder.Map("/jwks", "GET").WithHandler(HandleJwks);
        if (endpointOptions.AuthorizeEndpointEnabled)
            builder.Map("/authorize/start", "POST").WithHandler(HandleAuthorizeStart);
        if (endpointOptions.TrustIssueEndpointEnabled)
            // 已认证端点（不经 AllowAnonymous）——RequireAuthorization 追加认证要求
            builder.Map("/trust/issue", "POST", allowAnonymous: false).RequireAuthorization().WithHandler(HandleTrustIssue);
        if (endpointOptions.IdentityClaimEndpointEnabled)
            builder.Map("/identity/claim", "POST").WithHandler(HandleIdentityClaim);

        // 测试端点（默认关——生产禁用；联调验证 ticket 闭环用）——对齐正常端点风格：平台段 + 通道段 + ticket 段
        if (endpointOptions.TestTicketEndpointEnabled)
            builder.Map("/{platformId}/test/{channelId}/{ticket}", "GET").WithHandler(HandleTestTicket);
    }

    // ─────────────────────────── 端点处理器（匿名游客帧） ───────────────────────────

    /// <summary>登录编排（POST {prefix}/{platformId}/login[ /{channelId}]）——body/route 传 channelId + 参数（code 等）→
    /// <see cref="ISsoLogin"/> 门面 → <see cref="SsoChannelAuthResult"/> JSON；平台段 Ordinal 校验（防跨平台错配）+ B 守卫
    /// （多通道 + 省略 channelId → CHANNEL_REQUIRED，Oracle 评审条件 1）；FailReason 映射
    /// （CHANNEL_NOT_FOUND→404 / CHANNEL_REQUIRED→400 / CHANNEL_REGISTRY_UNAVAILABLE→503 / 其他业务码→400）；异常包裹（非取消）→ 503。</summary>
    private static async Task<IResult> HandleSsoLogin(HttpContext ctx, string platformId, string? channelId, [FromBody] SsoLoginWebRequest req, CancellationToken ct)
    {
        var registry = GetGuest(ctx).Use<IChannelRegistry>();
        var (cfg, failReason) = await ResolveChannelAsync(registry, platformId, channelId, ct);
        if (cfg is null)
            return MapFailReason(failReason);

        var context = new SsoChannelAuthContext(req.Parameters ?? new Dictionary<string, string?>());
        SsoChannelAuthResult result;
        try
        {
            // 已解析出具体通道实例（cfg.ChannelId）——精确选区（多通道守卫在 ResolveChannelAsync，单通道默认降级归一内部键）
            result = await GetGuest(ctx).Use<ISsoLogin>().LoginAsync(cfg.ChannelId, context, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 门面未拦截的异常（领域门面应收敛——兜底 registry 故障语义）
            return Results.Json(new ErrorResponse("CHANNEL_REGISTRY_UNAVAILABLE", null),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (result.Success)
            return Results.Ok(result);

        return MapFailReason(result.FailReason);
    }

    /// <summary>JWKS 公钥分发（GET {prefix}/jwks）——信任内核公共密钥分发（kid + x/y 坐标，RFC 7517），匿名公开。</summary>
    private static IResult HandleJwks(HttpContext ctx)
        => Results.Text(GetGuest(ctx).Use<IToken2Service>().GetJwksJson(), contentType: "application/json");

    /// <summary>平台事件推送接收（POST {prefix}/{platformId}/event[/{channelId}]）——端点只中转：原始 body 包参数字典
    /// （<c>{"__raw": rawBody, "channel_id": channelId}</c>）+ <b>透传 query 参数</b>（msg_signature/signature/
    /// timestamp/nonce/echostr——微信验签参数在 URL query，现有缺陷修复）→ 构造通道 → AuthenticateAsync 验签
    /// （平台库通道内部完成，一票否决）→ <b>事件编排</b>（uid 只读查询 + 平台化回复 + handler first-non-null-wins）
    /// → <b>微信 XML 被动回复</b>（text/xml——微信要求 XML 回复，JSON 致"服务暂不可用"）。
    /// <para><b>缺陷修复（2026-10-11 对外路由命名空间）</b>：channelType 按平台段推导 <c>{cfg.PlatformType}_event</c>
    /// 精确匹配 <c>*_event</c> 通道模板——原 <c>CreateAsync(channelId, null)</c> 走 null 分支（IsOAuthChannel
    /// 白名单仅 <c>*_oauth/*_oidc</c>）致 event 通道不可达（双注册错选 OAuth 通道 / 单注册 404）。</para>
    /// <para><b>URL 验证握手（echostr——微信回调 URL 验证契约，GET）</b>：query 含 echostr 即验证请求 →
    /// 通道 <c>VerifyUrlAsync</c> 双模式验签（安全 msg_signature 4 参 + AES 解密 echostr / 明文 signature 3 参）
    /// → 回显明文 echostr（text/plain）。安全模式回显密文必失败（微信后台统一"token 验证失败"）。</para>
    /// <para><b>微信 5 秒超时约束（外部身份联邦 E3）</b>：handler 委托为空 → 200 空回复（text/xml）兜底——
    /// 异步客服消息机制归 Phase 2/消费方（本端点不实现客服 API）。</para></summary>
    private static async Task<IResult> HandleEvent(HttpContext ctx, string platformId, string? channelId, CancellationToken ct)
    {
        var logger = ctx.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
            .CreateLogger("Federation.Web.Event");
        logger.LogInformation("微信事件端点收到请求：Method={Method} Path={Path} PlatformId={PlatformId} ChannelId={ChannelId}",
            ctx.Request.Method, ctx.Request.Path, platformId, channelId ?? "");

        var registry = GetGuest(ctx).Use<IChannelRegistry>();
        var (cfg, failReason) = await ResolveChannelAsync(registry, platformId, channelId, ct);
        if (cfg is null)
        {
            logger.LogWarning("微信事件端点通道解析失败：PlatformId={PlatformId} ChannelId={ChannelId} FailReason={FailReason}",
                platformId, channelId ?? "", failReason ?? "");
            return MapFailReason(failReason);
        }
        logger.LogInformation("微信事件端点通道解析成功：ChannelId={ChannelId} PlatformType={PlatformType}",
            cfg.ChannelId, cfg.PlatformType);

        string rawBody;
        using (var reader = new StreamReader(ctx.Request.Body))
            rawBody = await reader.ReadToEndAsync(ct);

        // 参数字典：原始 body __raw + channel_id + query 透传（微信验签参数在 URL query——现有缺陷修复）
        var parameters = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["__raw"] = rawBody,
            ["channel_id"] = cfg.ChannelId,
        };
        foreach (var (key, value) in ctx.Request.Query)
            parameters[key] = value.ToString();

        ISsoChannel? channel;
        try
        {
            // 按平台段推导 channelType（权威 = cfg.PlatformType，非路由段）——精确匹配 *_event 模板（缺陷修复）
            channel = await GetGuest(ctx).Use<ISsoChannelFactory>().CreateAsync(cfg.ChannelId, $"{cfg.PlatformType}_event", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(new ErrorResponse("CHANNEL_REGISTRY_UNAVAILABLE", null),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        if (channel is null)
            return Results.Json(new ErrorResponse("CHANNEL_NOT_FOUND", null),
                statusCode: StatusCodes.Status404NotFound);

        try
        {
            // URL 验证握手（GET——微信后台服务器配置验证契约，query 含 echostr 即验证请求）：
            // 通道 VerifyUrlAsync 双模式验签（安全 msg_signature 4 参 + AES 解密 echostr / 明文 signature 3 参）
            // → 回显明文 echostr（text/plain）。必须在 AuthenticateAsync 之前——验证请求无 msg_encrypt，
            // 走事件验签（要求 msg_encrypt）会被参数检查拦截（WECHAT_EVENT_PARAMS_MISSING）。
            if (parameters.TryGetValue("echostr", out var echo) && !string.IsNullOrEmpty(echo))
            {
                var echoPlain = await channel.VerifyUrlAsync(new ChannelInboundContext(parameters), ct);
                return echoPlain is null
                    ? Results.BadRequest(new ErrorResponse("WECHAT_URL_VERIFY_FAILED", null))
                    : Results.Text(echoPlain, contentType: "text/plain");
            }

            // 验签（信任根一票否决——平台库通道内部完成）；失败 → FailReason 映射 4xx
            var result = await channel.AuthenticateAsync(new SsoChannelAuthContext(parameters), ct);
            if (!result.Success)
                return Results.BadRequest(new ErrorResponse(result.FailReason ?? "AUTHENTICATION_FAILED", null));

            // 已验签外部身份（供编排 uid 只读查询——评审 F7：GetByExternalIdAsync 只读不建号）
            parameters["external_user_id"] = result.ExternalUserId ?? "";

            // 事件编排（uid 只读查询 + 平台化回复 + handler first-non-null-wins）→ 微信 XML 被动回复（text/xml）
            var reply = await GetGuest(ctx).Use<IEventOrchestrator>()
                .OrchestrateAsync(cfg.ChannelId, $"{cfg.PlatformType}_event", new ChannelInboundContext(parameters), ct);

            // 平台化回复（微信 XML text/xml）/ 空回复 200 兜底（微信 5 秒超时——异步客服消息归 Phase 2/消费方）
            return reply is null
                ? Results.Text("", contentType: "text/xml")
                : Results.Text(reply.Content, reply.ContentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Json(new ErrorResponse("CHANNEL_REGISTRY_UNAVAILABLE", null),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    // ─────────────────────────── 子应用消费方委托端点（ISsoSubAppBridge 编排，2026-10-11） ───────────────────────────

    /// <summary>发起外部认证（POST {prefix}/authorize/start，匿名）——<see cref="ISsoSubAppBridge.StartAsync"/>：
    /// redirect 白名单 + state 票据 + 通道授权 URL 构造。CallbackBaseUrl 由本 handler 用 Request.Scheme/Host
    /// <b>服务端可信填充</b>（OAuth redirect_uri 落点，防子应用伪造回调域）。建议消费方对前缀配 RateLimitingWebExtension
    /// IP 限流（Oracle 条件 5——匿名端点防滥用）。</summary>
    private static async Task<IResult> HandleAuthorizeStart(HttpContext ctx, [FromBody] AuthorizeStartRequest req, CancellationToken ct)
    {
        // 服务端可信回调基址（请求体值被覆盖——防子应用把 IdP 回调导向任意域）
        var request = req with { CallbackBaseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}" };
        var result = await GetGuest(ctx).Use<ISsoSubAppBridge>().StartAsync(request, ct);
        if (!result.Success)
            return MapFailReason(result.FailReason);
        return Results.Ok(new { authorize_url = result.AuthorizeUrl, state = result.State });
    }

    /// <summary>OAuth 回调落地（GET {prefix}/{platformId}/oauth[/{channelId}]/callback，匿名——<b>GET</b>：IdP 302 重定向语义，
    /// Oracle 条件 1）——query 原样包参数字典 → <see cref="ISsoSubAppBridge.CompleteOAuthAsync"/>（验 state 票据 →
    /// ISsoLogin 纯通道认证 → 签 accesscode expectedClaimant=app_id）→ 302 跳回子应用（code + sub_state）。
    /// 无通道段形态（{prefix}/{platformId}/oauth/callback）→ 单通道降级 / 多通道 CHANNEL_REQUIRED（B 守卫，Oracle 评审条件 1）。</summary>
    private static async Task<IResult> HandleOAuthCallback(HttpContext ctx, string platformId, string? channelId, CancellationToken ct)
    {
        var registry = GetGuest(ctx).Use<IChannelRegistry>();
        var (cfg, failReason) = await ResolveChannelAsync(registry, platformId, channelId, ct);
        if (cfg is null)
            return MapFailReason(failReason);

        var parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in ctx.Request.Query)
            parameters[key] = value.ToString();

        var result = await GetGuest(ctx).Use<ISsoSubAppBridge>().CompleteOAuthAsync(cfg.ChannelId, parameters, ct);
        if (!result.Success)
            return MapFailReason(result.FailReason);
        return Results.Redirect(result.RedirectUrl!);
    }

    /// <summary>Platform 登录态签发（POST {prefix}/trust/issue，<b>已认证</b> Bearer token1——direct 模式）——
    /// 已认证帧（AuthCenterWebExtension 中间件写入 <c>Items["DomainUser"]</c>）→
    /// <see cref="ISsoSubAppBridge.IssueForPlatformUserAsync"/>（验白名单 → 签 accesscode payload=Platform uid）→
    /// 302 跳回子应用（code）。未认证 → 401 UNAUTHENTICATED（不经 AllowAnonymous——与 AuthCenter 中间件短路判定配合）。</summary>
    private static async Task<IResult> HandleTrustIssue(HttpContext ctx, [FromBody] TrustIssueRequest req, CancellationToken ct)
    {
        if (ctx.Items["DomainUser"] is not DomainUser<TUserInfo> user || !user.IsAuthenticated)
            return Results.Json(new ErrorResponse("UNAUTHENTICATED", null),
                statusCode: StatusCodes.Status401Unauthorized);

        var result = await user.Use<ISsoSubAppBridge>().IssueForPlatformUserAsync(req, user.UserInfo.UserIdString, ct);
        if (!result.Success)
            return MapFailReason(result.FailReason);
        return Results.Redirect(result.RedirectUrl!);
    }

    /// <summary>子应用兑现（POST {prefix}/identity/claim，匿名 + 子应用 credential）——
    /// <see cref="ISsoSubAppBridge.ClaimAsync"/>（credential 鉴权统一 AUTHENTICATION_FAILED 防枚举 →
    /// RedeemAsync 原子取回 payload）→ 200 身份载荷 JSON。</summary>
    private static async Task<IResult> HandleIdentityClaim(HttpContext ctx, [FromBody] IdentityClaimRequest req, CancellationToken ct)
    {
        var result = await GetGuest(ctx).Use<ISsoSubAppBridge>().ClaimAsync(req, ct);
        if (!result.Success)
            return MapFailReason(result.FailReason);
        return Results.Ok(new
        {
            mode = result.Mode,
            channel_id = result.ChannelId,
            external_user_id = result.ExternalUserId,
            platform_uid = result.PlatformUid,
        });
    }

    /// <summary>测试端点（GET {prefix}/{platformId}/test/{channelId}/{ticket}，匿名游客帧，默认关）——ticket 闭环验证：
    /// 平台段 + 通道段选区校验（<see cref="ResolveChannelAsync"/>——对齐平台集成面统一入口，防跨平台错配/探测）
    /// → <c>ISealedTicketService&lt;FederatedIdentity&gt;.RedeemAsync(ticket)</c>（面①密封票据——v4.10.77 TicketRedeemContext
    /// CAS，拆分命名 ADR109 D4）原子取回 payload → 200 载荷 JSON
    /// （uid/channelId/openid/channelType——图文落地页联调验证，channel_id 取自签入载荷 FederatedIdentity.ChannelId，
    /// 单通道也可取出、与选区分支无关）；失败/过期/已用/核销人不符 → null → 404（契约语义）。</summary>
    private static async Task<IResult> HandleTestTicket(HttpContext ctx, string platformId, string channelId, string ticket, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            return Results.BadRequest(new ErrorResponse("TICKET_REQUIRED", null));

        var registry = GetGuest(ctx).Use<IChannelRegistry>();
        var (cfg, failReason) = await ResolveChannelAsync(registry, platformId, channelId, ct);
        if (cfg is null)
            return MapFailReason(failReason);

        try
        {
            var identity = await GetGuest(ctx).Use<ISealedTicketService<TKW.Framework.Domain.FederatedIdentity>>()
                .RedeemAsync(ticket, context: null, ct);
            if (identity is null)
                return Results.Json(new ErrorResponse("TICKET_NOT_FOUND", null),
                    statusCode: StatusCodes.Status404NotFound);

            return Results.Ok(new
            {
                ok = true,
                uid = identity.UId,
                channel_id = identity.ChannelId,
                external_user_id = identity.ExternalUserId,
                channel_type = identity.ChannelType,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 载荷解密失败等信任根问题——fail-closed 拒信任（错误码机器可读）；成功/失败/过期/已用由 null 分支覆盖
            var reason = ex.Message ?? "TICKET_REDEEM_FAILED";
            return Results.Json(new ErrorResponse(reason, null), statusCode: StatusCodes.Status400BadRequest);
        }
    }

    // ─────────────────────────── 辅助 ───────────────────────────

    /// <summary>游客帧（V0.5.2 模式延伸）——匿名端点经 guest.Use&lt;门面&gt;() 调用守卫工厂门面。
    /// ⚠️ 依赖 UseWebSession（ContextExtraction 阶段 2 写游客 DomainUser）；未装配 → 抛守卫 = 正确 fail。</summary>
    private static DomainUser<TUserInfo> GetGuest(HttpContext ctx)
    {
        if (ctx.Items["DomainUser"] is not DomainUser<TUserInfo> guest)
            throw new InvalidOperationException(
                "游客帧缺失——FederationWebExtension 须与 UseWebSession 同装配（ContextExtraction 写游客 DomainUser）。");
        return guest;
    }

    /// <summary>FailReason → HTTP 状态码映射（对齐子应用消费方错误码映射表——Oracle 条件 4 防枚举：claim 统一 401、
    /// 认证类 401 / 限流 429 / CHANNEL_REQUIRED→400（B 守卫多通道省略通道段，Oracle 评审条件 1）/
    /// CHANNEL_DISABLED→403 / CHANNEL_NOT_FOUND→404 / CHANNEL_REGISTRY_UNAVAILABLE→503 / 其他业务码→400）。</summary>
    private static IResult MapFailReason(string? failReason)
        => failReason switch
        {
            "AUTHENTICATION_FAILED" => Results.Json(new ErrorResponse("AUTHENTICATION_FAILED", null),
                statusCode: StatusCodes.Status401Unauthorized),
            "UNAUTHENTICATED" => Results.Json(new ErrorResponse("UNAUTHENTICATED", null),
                statusCode: StatusCodes.Status401Unauthorized),
            "RATE_LIMITED" => Results.Json(new ErrorResponse("RATE_LIMITED", null),
                statusCode: StatusCodes.Status429TooManyRequests),
            "CHANNEL_REQUIRED" => Results.Json(new ErrorResponse("CHANNEL_REQUIRED", null),
                statusCode: StatusCodes.Status400BadRequest),
            "CHANNEL_DISABLED" => Results.Json(new ErrorResponse("CHANNEL_DISABLED", null),
                statusCode: StatusCodes.Status403Forbidden),
            "CHANNEL_NOT_FOUND" => Results.Json(new ErrorResponse("CHANNEL_NOT_FOUND", null),
                statusCode: StatusCodes.Status404NotFound),
            "CHANNEL_REGISTRY_UNAVAILABLE" => Results.Json(new ErrorResponse("CHANNEL_REGISTRY_UNAVAILABLE", null),
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.BadRequest(new ErrorResponse(failReason ?? "LOGIN_FAILED", null)),
        };

    /// <summary>通道实例解析 + 平台段校验 + B 守卫（平台集成面端点统一入口，2026-10-11 对外路由命名空间）——
    /// 解析通道配置（channelId 空白 → 活跃通道枚举降级：0 → CHANNEL_NOT_FOUND、>1 → CHANNEL_REQUIRED、==1 → 唯一通道），
    /// 平台段 Ordinal 校验（<c>cfg.PlatformType == platformId</c>——不匹配统一 CHANNEL_NOT_FOUND 404 防跨平台错配/探测；
    /// Oracle 评审条件 2）。返回 (cfg, failReason)——failReason 非 null 时 cfg 为 null。</summary>
    private static async Task<(ChannelConfig? Cfg, string? FailReason)> ResolveChannelAsync(
        IChannelRegistry registry, string platformId, string? channelId, CancellationToken ct)
    {
        ChannelConfig? cfg;
        if (string.IsNullOrWhiteSpace(channelId))
        {
            // B 守卫（Oracle 评审条件 1）：多通道（活跃数 >1）+ 省略 channelId → CHANNEL_REQUIRED 硬失败；
            // 单通道（活跃数 ==1）→ 唯一活跃通道降级（无歧义）；0 → CHANNEL_NOT_FOUND。
            var active = (await registry.GetAllAsync(ct)).Where(c => c.IsEnabled).ToList();
            if (active.Count > 1)
                return (null, "CHANNEL_REQUIRED");
            cfg = active.FirstOrDefault();
            if (cfg is null)
                return (null, "CHANNEL_NOT_FOUND");
        }
        else
        {
            // 双键解析（alias → ChannelId）——对齐 GetByAliasOrIdAsync 语义（alias 全局解析，平台段为额外校验层，Oracle 评审条件 5c）
            cfg = await registry.GetByAliasOrIdAsync(channelId, ct);
            if (cfg is null)
                return (null, "CHANNEL_NOT_FOUND");
            if (!cfg.IsEnabled)
                return (null, "CHANNEL_DISABLED");
        }

        // 平台段 Ordinal 校验（统一 404 防枚举——不泄露"通道存在但平台写错" vs "通道不存在"，Oracle 评审条件 2）
        if (!string.Equals(cfg.PlatformType, platformId, StringComparison.Ordinal))
            return (null, "CHANNEL_NOT_FOUND");

        return (cfg, null);
    }

    /// <summary>端点 Options 解析（HealthCheck/AuthCenter 先例——IOptions → IConfiguration 直读 → 默认值 三段兜底）。</summary>
    private static FederationEndpointOptions ResolveOptions(IServiceProvider services)
    {
        var options = services.GetService<IOptions<FederationEndpointOptions>>()?.Value;
        if (options is not null) return options;
        var fallback = new FederationEndpointOptions();
        services.GetService<IConfiguration>()?.GetSection("TKWF:Federation:Web").Bind(fallback);
        return fallback;
    }
}

// ─────────────────────────── 请求/响应 DTO（Web 层语义，T3 装配面） ───────────────────────────

/// <summary>联邦登录请求（body 参数——code 等渠道参数，逐项透传 <see cref="SsoChannelAuthContext"/>）。</summary>
/// <param name="Parameters">渠道认证参数（可扩展键值——code/state 等；平台库通道按需读取）。</param>
public sealed record SsoLoginWebRequest(IReadOnlyDictionary<string, string?>? Parameters);

/// <summary>统一错误响应（错误码 + 可选消息）。</summary>
public sealed record ErrorResponse(string Code, string? Message);