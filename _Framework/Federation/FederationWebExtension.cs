using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Web.Extensions;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 联邦互联 Web 装配扩展（三层架构重构 T3 装配面——Federation 对外连接层 <c>/sso/*</c> 端点；v4.10.45
/// Web 装配钩子 ADR87/D22/G18）——自描述 Web 装配需求，消费方 <c>UseWebExtensions</c> 一次声明。
/// <para>锚点：默认 <see cref="MiddlewareAnchor.AfterRouting"/>（纯端点装配，无中间件——<see cref="ConfigureMiddleware"/>
/// 空实现）。端点均为匿名（<c>AllowAnonymous</c>）——/sso/* 是对外连接面（平台回调/公共 JWKS），
/// 不经 HttpAuthentication 认证（HttpAuth ExcludedPathPrefixes 归消费方装配配置）。</para>
/// <para>领域自治（G18 §3）：<see cref="ISsoLogin"/>/<see cref="ISsoChannelFactory"/> 等领域门面留 Domain 钩子
/// <see cref="FederationExtensionInitializer{TUserInfo}"/> 注册（AddConstructibleService 守卫工厂）——本 Web
/// 钩子仅宿主注册（端点映射 + 端点 Options），handler 经游客帧 <c>guest.Use&lt;门面&gt;()</c> 帧内解析。</para>
/// <para>配置分层（AGENTS §8）：领域决策在 <see cref="FederationChannelRegistryOptions"/>（TKWF:Federation:ChannelRegistry——
/// 通道启用/加密密钥路径）/ TrustCenter（TKWF:TrustCenter——令牌签发/密钥路径）不动；暴露面（RoutePrefix +
/// 端点开关）在 <see cref="FederationEndpointOptions"/>（TKWF:Federation:Web——不镜像领域配置）。</para>
/// <para><b>端点（7+1 形态，子应用消费方接入 2026-10-11）</b>：
/// <c>POST {prefix}/login</c>（无 channelId → <see cref="ISsoLogin.LoginDefaultAsync"/> 降级默认通道）/
/// <c>POST {prefix}/login/{channelId}</c>（精确选区 → <see cref="ISsoLogin.LoginAsync"/>）——
/// body/route 传参数（code 等）→ <c>SsoChannelAuthContext</c> → <see cref="SsoChannelAuthResult"/> JSON；
/// <c>GET {prefix}/jwks</c>——JWKS 公钥分发（信任内核职责，经 <see cref="IToken2Service.GetJwksJson"/>）；
/// <c>POST {prefix}/event/{channelId}</c>——平台事件推送接收（端点只中转：原始 body 包参数字典
/// <c>{"__raw": ..., "channel_id": ...}</c> → <see cref="ISsoChannelFactory.CreateAsync"/> 构造 →
/// <c>channel.AuthenticateAsync</c>——事件验签由平台库通道实现内部完成，一票否决）；
/// <b>子应用消费方委托（<see cref="ISsoSubAppBridge"/> 编排）</b>：<c>POST {prefix}/authorize/start</c>（匿名——
/// 发起外部认证，redirect 白名单 + state 票据 + 通道授权 URL 构造）、<c>GET {prefix}/oauth/{channelId}/callback</c>
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

    /// <summary>端点映射（AfterRouting 排空）——对外连接层 <c>/sso/*</c> 端点（登录编排 / JWKS / 事件接收 /
    /// 子应用消费方委托四端点），路径/开关经 <see cref="FederationEndpointOptions"/> 可配置。</summary>
    public void ConfigureEndpoints(IEndpointRouteBuilder endpoints, DomainWebOptions options)
    {
        var endpointOptions = ResolveOptions(endpoints.ServiceProvider);
        var prefix = endpointOptions.RoutePrefix.TrimEnd('/');

        if (endpointOptions.LoginEndpointEnabled)
        {
            endpoints.MapPost($"{prefix}/login", HandleSsoLogin).AllowAnonymous();
            endpoints.MapPost($"{prefix}/login/{{channelId}}", HandleSsoLogin).AllowAnonymous();
        }
        if (endpointOptions.JwksEndpointEnabled)
            endpoints.MapGet($"{prefix}/jwks", HandleJwks).AllowAnonymous();
        if (endpointOptions.EventEndpointEnabled)
            endpoints.MapPost($"{prefix}/event/{{channelId}}", HandleEvent).AllowAnonymous();
        if (endpointOptions.AuthorizeEndpointEnabled)
            endpoints.MapPost($"{prefix}/authorize/start", HandleAuthorizeStart).AllowAnonymous();
        if (endpointOptions.OauthCallbackEndpointEnabled)
            endpoints.MapGet($"{prefix}/oauth/{{channelId}}/callback", HandleOAuthCallback).AllowAnonymous();
        if (endpointOptions.TrustIssueEndpointEnabled)
            endpoints.MapPost($"{prefix}/trust/issue", HandleTrustIssue);   // 已认证端点（不经 AllowAnonymous）
        if (endpointOptions.IdentityClaimEndpointEnabled)
            endpoints.MapPost($"{prefix}/identity/claim", HandleIdentityClaim).AllowAnonymous();
    }

    // ─────────────────────────── 端点处理器（匿名游客帧） ───────────────────────────

    /// <summary>登录编排（POST {prefix}/login[ /{channelId}]）——body/route 传 channelId + 参数（code 等）→
    /// <see cref="ISsoLogin"/> 门面 → <see cref="SsoChannelAuthResult"/> JSON；FailReason 映射
    /// （CHANNEL_NOT_FOUND→404 / CHANNEL_REGISTRY_UNAVAILABLE→503 / 其他业务码→400）；异常包裹（非取消）→ 503。</summary>
    private static async Task<IResult> HandleSsoLogin(HttpContext ctx, string? channelId, [FromBody] SsoLoginWebRequest req, CancellationToken ct)
    {
        var context = new SsoChannelAuthContext(req.Parameters ?? new Dictionary<string, string?>());
        SsoChannelAuthResult result;
        try
        {
            result = string.IsNullOrWhiteSpace(channelId)
                ? await GetGuest(ctx).Use<ISsoLogin>().LoginDefaultAsync(context, ct)
                : await GetGuest(ctx).Use<ISsoLogin>().LoginAsync(channelId, context, ct);
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

    /// <summary>平台事件推送接收（POST {prefix}/event/{channelId}）——端点只中转：原始 body 包参数字典
    /// （<c>{"__raw": rawBody, "channel_id": channelId}</c>）→ 构造通道 → AuthenticateAsync；事件验签由
    /// 平台库通道实现内部完成（WeChatEventCrypto 一票否决），端点不解析事件语义。</summary>
    private static async Task<IResult> HandleEvent(HttpContext ctx, string channelId, CancellationToken ct)
    {
        string rawBody;
        using (var reader = new StreamReader(ctx.Request.Body))
            rawBody = await reader.ReadToEndAsync(ct);

        var parameters = new Dictionary<string, string?>
        {
            ["__raw"] = rawBody,
            ["channel_id"] = channelId,
        };

        ISsoChannel? channel;
        try
        {
            channel = await GetGuest(ctx).Use<ISsoChannelFactory>().CreateAsync(channelId, null, ct);
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
            var result = await channel.AuthenticateAsync(new SsoChannelAuthContext(parameters), ct);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(new ErrorResponse(result.FailReason ?? "AUTHENTICATION_FAILED", null));
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

    /// <summary>OAuth 回调落地（GET {prefix}/oauth/{channelId}/callback，匿名——<b>GET</b>：IdP 302 重定向语义，
    /// Oracle 条件 1）——query 原样包参数字典 → <see cref="ISsoSubAppBridge.CompleteOAuthAsync"/>（验 state 票据 →
    /// ISsoLogin 纯通道认证 → 签 accesscode expectedClaimant=app_id）→ 302 跳回子应用（code + sub_state）。</summary>
    private static async Task<IResult> HandleOAuthCallback(HttpContext ctx, string channelId, CancellationToken ct)
    {
        var parameters = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in ctx.Request.Query)
            parameters[key] = value.ToString();

        var result = await GetGuest(ctx).Use<ISsoSubAppBridge>().CompleteOAuthAsync(channelId, parameters, ct);
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
    /// 认证类 401 / 限流 429 / CHANNEL_NOT_FOUND→404 / CHANNEL_REGISTRY_UNAVAILABLE→503 / 其他业务码→400）。</summary>
    private static IResult MapFailReason(string? failReason)
        => failReason switch
        {
            "AUTHENTICATION_FAILED" => Results.Json(new ErrorResponse("AUTHENTICATION_FAILED", null),
                statusCode: StatusCodes.Status401Unauthorized),
            "UNAUTHENTICATED" => Results.Json(new ErrorResponse("UNAUTHENTICATED", null),
                statusCode: StatusCodes.Status401Unauthorized),
            "RATE_LIMITED" => Results.Json(new ErrorResponse("RATE_LIMITED", null),
                statusCode: StatusCodes.Status429TooManyRequests),
            "CHANNEL_NOT_FOUND" => Results.Json(new ErrorResponse("CHANNEL_NOT_FOUND", null),
                statusCode: StatusCodes.Status404NotFound),
            "CHANNEL_REGISTRY_UNAVAILABLE" => Results.Json(new ErrorResponse("CHANNEL_REGISTRY_UNAVAILABLE", null),
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.BadRequest(new ErrorResponse(failReason ?? "LOGIN_FAILED", null)),
        };

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