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
/// <para><b>端点（3+1 形态）</b>：<c>POST {prefix}/login</c>（无 channelId → <see cref="ISsoLogin.LoginDefaultAsync"/>
/// 降级默认通道）/ <c>POST {prefix}/login/{channelId}</c>（精确选区 → <see cref="ISsoLogin.LoginAsync"/>）——
/// body/route 传参数（code 等）→ <c>SsoChannelAuthContext</c> → <see cref="SsoChannelAuthResult"/> JSON；
/// <c>GET {prefix}/jwks</c>——JWKS 公钥分发（信任内核职责，经 <see cref="IToken2Service.GetJwksJson"/>）；
/// <c>POST {prefix}/event/{channelId}</c>——平台事件推送接收（端点只中转：原始 body 包参数字典
/// <c>{"__raw": ..., "channel_id": ...}</c> → <see cref="ISsoChannelFactory.CreateAsync"/> 构造 →
/// <c>channel.AuthenticateAsync</c>——事件验签由平台库通道实现内部完成，一票否决）。</para>
/// <para>⚠️ 装配约束：须与 <c>UseWebSession</c> 同装配（ContextExtraction 阶段 2 写游客 DomainUser——
/// 匿名端点 handler 经游客帧 <c>guest.Use&lt;门面&gt;()</c> 调用守卫工厂门面；未装配 → 抛守卫 = 正确 fail
/// 非静默降级）。</para>
/// <para>不提供 authorize 端点：<see cref="ISsoChannel"/> 契约无 <c>BuildAuthorizeUrl</c>——不臆造契约，
/// 授权链接构造归平台库/消费方（N3 P1-3 state 归装配层——如需 OAuth 跳转端点由平台库侧另行装配）。</para>
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

    /// <summary>端点映射（AfterRouting 排空）——对外连接层 <c>/sso/*</c> 端点（登录编排 / JWKS / 事件接收），路径/开关经 <see cref="FederationEndpointOptions"/> 可配置。</summary>
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

    /// <summary>FailReason → HTTP 状态码映射（对齐 SsoLogin 错误码映射表：CHANNEL_NOT_FOUND→404 /
    /// CHANNEL_REGISTRY_UNAVAILABLE→503 / 其他业务码→400）。</summary>
    private static IResult MapFailReason(string? failReason)
        => failReason switch
        {
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