using System;
using System.Security.Authentication;
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

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 认证中心 Web 装配扩展（V0.6.0 归层迭代 T4——原 <c>JwtAuthenticationWebExtension</c> 归层更名 +
/// 内建标准对内端点；v4.10.45 Web 装配钩子 ADR87/D22/G18）——自描述 Web 装配需求，消费方
/// <c>UseWebExtensions</c> 一次声明。
/// <para>锚点：默认 <see cref="MiddlewareAnchor.BeforeAuthentication"/>——ContextExtraction 之后、
/// HttpAuthentication 之前执行（验签恢复 DomainUser 供框架认证判定——P0-1 锚点分桶设计目标）。</para>
/// <para>领域自治（G18 §3）：<see cref="ITokenVerifier"/> 等业务服务留 Domain 钩子
/// <see cref="AuthCenterExtensionInitializer{TUserInfo}"/> 注册——本 Web 钩子仅宿主注册（中间件 + 端点映射）。</para>
/// <para>配置分层（AGENTS §8）：领域决策在 <see cref="AuthCenterOptions"/>（TKWF:AuthCenter）；
/// 暴露面（RoutePrefix + 端点开关）在 <see cref="AuthCenterEndpointOptions"/>（TKWF:AuthCenter:Web——
/// 不镜像领域配置，领域安全行为 Web 层不可关闭）；中间件行为（RejectInvalidToken）在
/// <see cref="AuthCenterMiddlewareOptions"/>（独立 POCO）。</para>
/// <para>委托直传（G18 §3，Oracle P0-2）：<see cref="RestoreUser"/> 实例属性 → <see cref="ConfigureMiddleware"/>
/// 内 <c>UseMiddleware&lt;JwtAuthenticationMiddleware{TUserInfo}&gt;(RestoreUser)</c>（不经 Options——Options 是 POCO）。</para>
/// <para>⚠️ 装配约束（V0.5.2 延伸）：须与 <c>UseWebSession</c> 同装配（ContextExtraction 阶段 2 写游客 DomainUser——
/// 匿名端点 handler 经游客帧 <c>guest.Use&lt;门面&gt;()</c> 调用守卫工厂门面；未装配 → 抛守卫 = 正确 fail 非静默降级）。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public sealed class AuthCenterWebExtension<TUserInfo> : IWebExtension
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>消费方恢复委托（验签结果 → 已认证 DomainUser；UserHelper 子类内经 CreateUserInstance() 构建——需 Host 上下文）。
    /// <para>必填：消费方在 <c>Add&lt;AuthCenterWebExtension&lt;TUserInfo&gt;&gt;(x =&gt; x.RestoreUser = ...)</c> 设置；
    /// 未设置 → ConfigureMiddleware 抛 InvalidOperationException（启动期 fail-fast，避免运行时 NRE）。</para></summary>
    public Func<HttpContext, TokenValidationResult, Task<DomainUser<TUserInfo>>>? RestoreUser { get; set; }

    /// <summary>端点 Options 编程式配置（可选）——后于配置节 <c>TKWF:AuthCenter:Web</c> 应用。</summary>
    public Action<AuthCenterEndpointOptions>? ConfigureOptions { get; set; }

    /// <summary>中间件行为 Options 编程式配置（可选）——<see cref="AuthCenterMiddlewareOptions.RejectInvalidToken"/>（归层更名自 JwtAuthenticationOptions）。</summary>
    public Action<AuthCenterMiddlewareOptions>? ConfigureMiddlewareOptions { get; set; }

    /// <summary>宿主注册（Build 前立即执行；configure 委托先于本方法——实例属性已就绪，零延迟闭包）。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // 仅宿主注册：中间件行为 Options + 端点 Options（JwtAuthenticationMiddleware 经 IOptions 解析 RejectInvalidToken）
        services.AddOptions<AuthCenterMiddlewareOptions>();
        if (ConfigureMiddlewareOptions is not null)
            services.Configure(ConfigureMiddlewareOptions);
        services.AddOptions<AuthCenterEndpointOptions>();
        if (ConfigureOptions is not null)
            services.Configure(ConfigureOptions);
    }

    /// <summary>中间件挂载（BeforeAuthentication 锚点桶排空时执行）——RestoreUser 直传中间件构造参数，不经 Options。</summary>
    public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options)
    {
        if (RestoreUser is null)
            throw new InvalidOperationException(
                "AuthCenterWebExtension.RestoreUser 未设置——消费方须在 UseWebExtensions 的 Add 委托内赋值（如 x.RestoreUser = ...）。");
        app.UseMiddleware<JwtAuthenticationMiddleware<TUserInfo>>(RestoreUser);
    }

    /// <summary>端点映射（AfterRouting 排空）——内建标准对内端点（V0.6.0 T4），路径/开关经 <see cref="AuthCenterEndpointOptions"/> 可配置。</summary>
    public void ConfigureEndpoints(IEndpointRouteBuilder endpoints, DomainWebOptions options)
    {
        var endpointOptions = ResolveOptions(endpoints.ServiceProvider);
        var prefix = endpointOptions.RoutePrefix.TrimEnd('/');

        if (endpointOptions.SmsSendCodeEndpointEnabled)
            endpoints.MapPost($"{prefix}/sms/send-code", HandleSendSmsCode).AllowAnonymous();
        if (endpointOptions.SmsLoginEndpointEnabled)
            endpoints.MapPost($"{prefix}/login/sms", HandleSmsLogin).AllowAnonymous();
        if (endpointOptions.WechatLoginEndpointEnabled)
            endpoints.MapPost($"{prefix}/login/wechat", HandleWechatLogin).AllowAnonymous();
        if (endpointOptions.RefreshEndpointEnabled)
            endpoints.MapPost($"{prefix}/refresh", HandleRefresh).AllowAnonymous();
        if (endpointOptions.LogoutEndpointEnabled)
            endpoints.MapPost($"{prefix}/logout", HandleLogout);          // 不 AllowAnonymous——需 Bearer 认证
        if (endpointOptions.TicketExchangeEndpointEnabled)
            endpoints.MapPost($"{prefix}/ticket/exchange", HandleTicketExchange).AllowAnonymous();
    }

    // ─────────────────────────── 端点处理器（匿名帧 / 已认证帧） ───────────────────────────

    private static async Task<IResult> HandleSendSmsCode(HttpContext ctx, [FromBody] SendSmsCodeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Phone))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "phone 必填"));
        if (req.Scene is not (SmsScenes.Login or SmsScenes.Register or SmsScenes.Bind or SmsScenes.Reset))
            return Results.BadRequest(new ErrorResponse("INVALID_SCENE", "scene 白名单：Login/Register/Bind/Reset"));

        try
        {
            await GetGuest(ctx).Use<ISmsVerificationService>().SendCodeAsync(req.Phone, req.Scene, ct);
            return Results.NoContent();
        }
        catch (SmsMockForbiddenException ex)
        {
            return Results.Json(new ErrorResponse("SMS_SENDER_NOT_CONFIGURED", ex.Message),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (AuthenticationException ex) when (IsRateLimitedCode(ex.Message))
        {
            return Results.Json(new ErrorResponse(ex.Message, null), statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (AuthenticationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, null));
        }
    }

    private static async Task<IResult> HandleSmsLogin(HttpContext ctx, [FromBody] SmsLoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.Code))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "phone/code 必填"));

        try
        {
            var result = await GetGuest(ctx).Use<ISmsLoginService>().LoginAsync(req.Phone, req.Code, ct);
            return result.Success
                ? Results.Ok(ToTokenResponse(result.Token!))
                : Results.BadRequest(new ErrorResponse(result.FailReason ?? "LOGIN_FAILED", null));
        }
        catch (SmsMockForbiddenException ex)
        {
            return Results.Json(new ErrorResponse("SMS_SENDER_NOT_CONFIGURED", ex.Message),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (AuthenticationException ex) when (IsRateLimitedCode(ex.Message))
        {
            return Results.Json(new ErrorResponse(ex.Message, null), statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (AuthenticationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, null));
        }
    }

    private static async Task<IResult> HandleWechatLogin(HttpContext ctx, [FromBody] WechatLoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "code 必填"));

        try
        {
            var result = await GetGuest(ctx).Use<IWechatLoginService>().LoginAsync(req.Code, req.Scope, ct);
            return result.Success
                ? Results.Ok(ToTokenResponse(result.Token!))
                : Results.BadRequest(new ErrorResponse(result.FailReason ?? "LOGIN_FAILED", null));
        }
        catch (AuthenticationException ex) when (IsRateLimitedCode(ex.Message))
        {
            return Results.Json(new ErrorResponse(ex.Message, null), statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (AuthenticationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, null));
        }
    }

    private static async Task<IResult> HandleRefresh(HttpContext ctx, [FromBody] RefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "refresh_token 必填"));

        try
        {
            var result = await GetGuest(ctx).Use<ITokenService>().RefreshTokenAsync(req.RefreshToken, ct);
            return Results.Ok(ToTokenResponse(result));
        }
        catch (AuthenticationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, null));
        }
    }

    private static async Task<IResult> HandleLogout(HttpContext ctx, CancellationToken ct)
    {
        // 已认证端点：中间件验签后 RestoreUser 已替换 Items["DomainUser"] + 写入 TokenValidationResult（V0.6.0 T4）
        if (ctx.Items["DomainUser"] is not DomainUser<TUserInfo> user)
            return Results.Unauthorized();
        if (ctx.Items[JwtAuthenticationMiddleware<TUserInfo>.TokenValidationResultKey] is not TokenValidationResult tr)
            return Results.Unauthorized();

        await user.Use<ITokenService>().RevokeTokenAsync(tr.Jti, "logout", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleTicketExchange(HttpContext ctx, [FromBody] TicketExchangeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Ticket) || string.IsNullOrWhiteSpace(req.AppId))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "ticket/app_id 必填"));

        try
        {
            var result = await GetGuest(ctx).Use<IOAuthTicketService>().ExchangeAsync(
                new OAuthTicketExchangeRequest(req.Ticket, req.CodeVerifier, req.AppId, req.State), ct);
            return Results.Ok(ToTokenResponse(result));
        }
        catch (AuthenticationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, null));
        }
    }

    // ─────────────────────────── 辅助 ───────────────────────────

    /// <summary>游客帧（V0.5.2 模式延伸）——匿名端点经 guest.Use&lt;门面&gt;() 调用守卫工厂门面。
    /// ⚠️ 依赖 UseWebSession（ContextExtraction 阶段 2 写游客 DomainUser）；未装配 → 抛守卫 = 正确 fail。</summary>
    private static DomainUser<TUserInfo> GetGuest(HttpContext ctx)
    {
        if (ctx.Items["DomainUser"] is not DomainUser<TUserInfo> guest)
            throw new InvalidOperationException(
                "游客帧缺失——AuthCenterWebExtension 须与 UseWebSession 同装配（ContextExtraction 写游客 DomainUser）。");
        return guest;
    }

    /// <summary>领域频控错误码（Message 前辍判定——SmsVerificationService 抛 AuthenticationException + 频控码）。</summary>
    private static bool IsRateLimitedCode(string message)
        => message.StartsWith("SMS_RESEND_TOO_FREQUENT", StringComparison.Ordinal)
        || message.StartsWith("SMS_HOURLY_LIMIT", StringComparison.Ordinal)
        || message.StartsWith("SMS_DAILY_LIMIT", StringComparison.Ordinal)
        || message.StartsWith("SMS_VERIFY_ATTEMPT_LIMIT", StringComparison.Ordinal);

    private static TokenResponse ToTokenResponse(TokenIssueResult r)
        => new(r.AccessToken, r.RefreshToken, "Bearer", r.ExpiresIn);

    private static TokenResponse ToTokenResponse(TokenRefreshResult r)
        => new(r.AccessToken, r.RefreshToken, "Bearer", r.ExpiresIn);

    private static TokenResponse ToTokenResponse(OAuthTicketExchangeResult r)
        => new(r.AccessToken, r.RefreshToken, "Bearer", r.ExpiresIn);

    /// <summary>端点 Options 解析（HealthCheck 先例——IOptions → IConfiguration 直读 → 默认值 三段兜底）。</summary>
    private static AuthCenterEndpointOptions ResolveOptions(IServiceProvider services)
    {
        var options = services.GetService<IOptions<AuthCenterEndpointOptions>>()?.Value;
        if (options is not null) return options;
        var fallback = new AuthCenterEndpointOptions();
        services.GetService<IConfiguration>()?.GetSection("TKWF:AuthCenter:Web").Bind(fallback);
        return fallback;
    }
}

// ─────────────────────────── 请求/响应 DTO（Web 层语义，V0.6.0 T4） ───────────────────────────

/// <summary>令牌响应（标准形态——access_token/refresh_token/token_type/expires_in）。</summary>
/// <param name="AccessToken">JWT access token（RS256）。</param>
/// <param name="RefreshToken">Refresh token（rotation——SHA256 落库）。</param>
/// <param name="TokenType">固定 <c>Bearer</c>。</param>
/// <param name="ExpiresIn">access token 有效期（秒）。</param>
public sealed record TokenResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

/// <summary>统一错误响应（错误码 + 可选消息）。</summary>
public sealed record ErrorResponse(string Code, string? Message);

/// <summary>短信验证码发送请求。</summary>
public sealed record SendSmsCodeRequest(string Phone, string Scene);

/// <summary>短信登录请求。</summary>
public sealed record SmsLoginRequest(string Phone, string Code);

/// <summary>微信登录请求（scope：snsapi_base 网页 / snsapi_login 扫码——透传）。</summary>
public sealed record WechatLoginRequest(string Code, string? Scope);

/// <summary>刷新请求。</summary>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>票据换令牌请求（PKCE code_verifier 可选——IssueAsync 时若生成则 Exchange 必传）。</summary>
public sealed record TicketExchangeRequest(string Ticket, string? CodeVerifier, string AppId, string? State);
