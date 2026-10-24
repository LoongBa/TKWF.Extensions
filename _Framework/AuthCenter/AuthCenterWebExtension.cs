using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Text.Json.Serialization;
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

    /// <summary>协议适配端点声明（Oracle C+E 2026-10-24——ConfigureEndpoints 删除，F13）——内建标准对内端点
    /// （V0.6.0 T4 + V0.8.0 认证 API 补全 + T5/V0.10.0 端点泛化），路径/开关经 <see cref="AuthCenterEndpointOptions"/> 可配置。
    /// 经窄接口 <see cref="IProtocolEndpointBuilder"/> 声明（路由模板相对组前缀；前缀拼接归框架 Module 前缀模型——
    /// 组 = basePath /api + DefaultPrefix auth = /api/auth，与旧 RoutePrefix 默认值一致，消费方零迁移；
    /// R2-3 无 builder 门禁保持）。handler 保留（内建 FailReason→HTTP 映射 + TokenResponse 双键序列化——
    /// MapGenerated 生成化作为后续批次，见 §9.15）。
    /// <para>T5（2026-10-09 三层边界）：<c>POST {prefix}/login/wechat</c> → <c>POST {prefix}/login/external/{{channelType}}</c>
    /// （外部 IdP 桥接登录——channelType 为平台库通道类型如 <c>wechat_oauth</c>/<c>qq_oauth</c>，body = 参数字典）。</para></summary>
    public void DeclareProtocolEndpoints(IProtocolEndpointBuilder builder)
    {
        var endpointOptions = ResolveOptions(builder.ServiceProvider);

        if (endpointOptions.SmsSendCodeEndpointEnabled)
            builder.Map("/sms/send-code", "POST").WithHandler(HandleSendSmsCode);
        if (endpointOptions.SmsVerifyEndpointEnabled)
            builder.Map("/sms/verify", "POST").WithHandler(HandleSmsVerify);  // V0.8.0 验证码独立校验
        if (endpointOptions.SmsLoginEndpointEnabled)
            builder.Map("/login/sms", "POST").WithHandler(HandleSmsLogin);
        if (endpointOptions.ExternalLoginEndpointEnabled)
            builder.Map("/login/external/{channelType}", "POST").WithHandler(HandleExternalLogin);  // T5（2026-10-09）替代 login/wechat
        if (endpointOptions.TicketLoginEndpointEnabled)
            builder.Map("/login/ticket", "POST").WithHandler(HandleExternalTicketLogin);  // V0.10.0 票据 SPA 兑现（独立路由——不挂 external 前缀，P2-1）
        if (endpointOptions.PasswordLoginEndpointEnabled)
            builder.Map("/login/password", "POST").WithHandler(HandlePasswordLogin);  // V0.9.0 B.9（P1-NEW-2 对称补全）
        if (endpointOptions.RefreshEndpointEnabled)
            builder.Map("/refresh", "POST").WithHandler(HandleRefresh);
        if (endpointOptions.LogoutEndpointEnabled)
            builder.Map("/logout", "POST").WithHandler(HandleLogout);          // 不 AllowAnonymous——需 Bearer 认证（handler 自检 Items 已认证帧）
        if (endpointOptions.TokenVerifyEndpointEnabled)
            builder.Map("/verify", "GET").WithHandler(HandleVerify);           // V0.8.0 已认证内省——不 AllowAnonymous（Bearer 必需，handler 自检）
        if (endpointOptions.GrantsEndpointEnabled)
            builder.Map("/grants", "GET").WithHandler(HandleGrants);           // V0.8.0 应用授权状态——不 AllowAnonymous（Bearer 必需，handler 自检）
        if (endpointOptions.TicketExchangeEndpointEnabled)
            builder.Map("/ticket/exchange", "POST").WithHandler(HandleTicketExchange);
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

    private static async Task<IResult> HandlePasswordLogin(HttpContext ctx, [FromBody] PasswordLoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Identifier)
            || string.IsNullOrWhiteSpace(req.ClientHash) || string.IsNullOrWhiteSpace(req.Salt))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "identifier/clientHash/salt 必填"));

        try
        {
            // V0.9.0（B.9 + ADR 决策 1）：密码登录编排门面（帧内经游客帧 Use）——SecurePassword 协议（客户端算 clientHash+salt，服务端零明文）
            var result = await GetGuest(ctx).Use<IPasswordLoginService>().LoginAsync(req.Identifier, req.ClientHash, req.Salt, ct);
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

    /// <summary>
    /// 外部 IdP 登录端点（T5 2026-10-09——替代已删 login/wechat）：POST {prefix}/login/external/{channelType}。
    /// body = 参数字典（含 code/channel_id 等平台协议字段——原样透传桥接）；游客帧 →
    /// <c>guest.Use&lt;IExternalIdpLoginService&gt;()</c> 编排（桥接认证 fail-hard：Federation 未装配 → 守卫异常 → 503）。
    /// </summary>
    private static async Task<IResult> HandleExternalLogin(HttpContext ctx, string channelType, [FromBody] Dictionary<string, string?> parameters, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(channelType))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "channelType 必填"));
        if (parameters is null || !parameters.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "code 必填"));

        try
        {
            var result = await GetGuest(ctx).Use<IExternalIdpLoginService>().LoginAsync(channelType, parameters, ct);
            return result.Success
                ? Results.Ok(ToTokenResponse(result.Token!))
                : Results.BadRequest(new ErrorResponse(result.FailReason ?? "LOGIN_FAILED", null));
        }
        catch (InvalidOperationException ex) when (IsDomainGuardException(ex))
        {
            // fail-hard（P7）：Federation 未装配 → User.Use<IExternalIdpAuthenticator>() 抛守卫 → 503 显式告知
            return Results.Json(new ErrorResponse("EXTERNAL_IDP_NOT_CONFIGURED", ex.Message),
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

    /// <summary>
    /// 外部 IdP 票据 SPA 兑现端点（V0.10.0——事件取 OpenId 链路"票 → token1"前端兑换面；P2-1 独立路由
    /// <c>POST {prefix}/login/ticket</c>，不挂 external 前缀）：body = {ticket, channel_id?}；游客帧 →
    /// <c>guest.Use&lt;IExternalTicketLoginService&gt;()</c> 验票（RedeemAsync null → TICKET_INVALID）+ 共享编排
    /// （P2-2 orchestrator）→ TokenResponse。fail-hard：ISealedTicketService 未装配（TrustCenter 未启用）
    /// → 守卫异常 → 503 TICKET_SERVICE_NOT_CONFIGURED。
    /// </summary>
    private static async Task<IResult> HandleExternalTicketLogin(HttpContext ctx, [FromBody] ExternalTicketLoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Ticket))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "ticket 必填"));

        try
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var result = await GetGuest(ctx).Use<IExternalTicketLoginService>().LoginAsync(req.Ticket, req.EffectiveChannelId, ip, ct);
            return result.Success
                ? Results.Ok(ToTokenResponse(result.Token!))
                : Results.BadRequest(new ErrorResponse(result.FailReason ?? "LOGIN_FAILED", null));
        }
        catch (InvalidOperationException ex) when (IsDomainGuardException(ex))
        {
            // fail-hard：ISealedTicketService（主框架契约，TrustCenter DB 后端注册）/ orchestrator 未装配 → 守卫异常 → 503
            return Results.Json(new ErrorResponse("TICKET_SERVICE_NOT_CONFIGURED", ex.Message),
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

    private static async Task<IResult> HandleRefresh(HttpContext ctx, [FromBody] RefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.EffectiveRefreshToken))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "refresh_token 必填"));

        try
        {
            var result = await GetGuest(ctx).Use<ITokenService>().RefreshTokenAsync(req.EffectiveRefreshToken, ct);
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
        if (string.IsNullOrWhiteSpace(req.Ticket) || string.IsNullOrWhiteSpace(req.EffectiveAppId))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "ticket/app_id 必填"));

        try
        {
            var result = await GetGuest(ctx).Use<IOAuthTicketService>().ExchangeAsync(
                new OAuthTicketExchangeRequest(req.Ticket, req.EffectiveCodeVerifier, req.EffectiveAppId, req.State), ct);
            return Results.Ok(ToTokenResponse(result));
        }
        catch (AuthenticationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, null));
        }
    }

    // ─────────────────────────── V0.8.0 认证 API 补全（3 端点） ───────────────────────────

    private static IResult HandleVerify(HttpContext ctx)
    {
        // 已认证内省（V0.8.0）：判别器 = 中间件验签产物 Items["AuthCenter.TokenValidationResult"]（唯一验签标记——
        // 非 DomainUser——UseWebSession 装配下匿名游客也是 DomainUser<TUserInfo>(IsAuthenticated=false)，
        // 只查 DomainUser 无法区分；TokenValidationResult 仅成功验签后写入，缺即未认证 → 401 显式拒绝）。
        // ⚠️ 401 语义：已认证内省（authenticated introspection）——无效 Bearer 返回 401 而非 RFC 7662 200{active:false}。
        if (ctx.Items[JwtAuthenticationMiddleware<TUserInfo>.TokenValidationResultKey] is not TokenValidationResult tr)
            return Results.Unauthorized();

        return Results.Ok(new VerifyResponse(
            Valid: true,                       // 到达即已验签
            UserId: tr.UserId,
            AuthType: tr.AuthType,
            AuthLevel: tr.AuthLevel,
            Exp: new DateTimeOffset(tr.ExpiresAtUtc).ToUnixTimeSeconds(),
            Jti: tr.Jti));
    }

    private static async Task<IResult> HandleGrants(HttpContext ctx, CancellationToken ct)
    {
        // 已认证端点（Bearer 必需，不 AllowAnonymous）：判别器 = 中间件验签产物 Items[TokenValidationResult]
        // （唯一验签标记——仅成功验签后写入，缺即未认证 → 401 显式拒绝；对齐 HandleLogout 双检形态）
        if (ctx.Items[JwtAuthenticationMiddleware<TUserInfo>.TokenValidationResultKey] is not TokenValidationResult tr)
            return Results.Unauthorized();
        if (ctx.Items["DomainUser"] is not DomainUser<TUserInfo> user)
            return Results.Unauthorized();

        var appId = ctx.Request.Query["app_id"].ToString();
        // userId 取验签产物 sub（tr.UserId——JWT sub 与 AuthGrantEntity.UserId 同源，平台内部 id）
        var grants = await user.Use<IAuthGrantQueryService>().GetGrantsAsync(
            tr.UserId, string.IsNullOrWhiteSpace(appId) ? null : appId, ct);

        return Results.Ok(new GrantsResponse(grants.Select(g => new GrantView(
            AppId: g.AppId,
            Scopes: g.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ValidUntil: g.ValidUntil,
            Source: g.Source,
            Status: g.Status)).ToList()));
    }

    private static async Task<IResult> HandleSmsVerify(HttpContext ctx, [FromBody] SmsVerifyRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.Code))
            return Results.BadRequest(new ErrorResponse("INVALID_ARGUMENT", "phone/code 必填"));
        if (req.Scene is not (SmsScenes.Login or SmsScenes.Register or SmsScenes.Bind or SmsScenes.Reset))
            return Results.BadRequest(new ErrorResponse("INVALID_SCENE", "scene 白名单：Login/Register/Bind/Reset"));

        try
        {
            var verified = await GetGuest(ctx).Use<ISmsVerificationService>().VerifyCodeAsync(req.Phone, req.Code, req.Scene, ct);
            return Results.Ok(new SmsVerifyResponse(verified));
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

    /// <summary>领域频控错误码（Message 前辍判定——SmsVerificationService/ExternalTicketLoginService 抛 AuthenticationException + 频控码）。</summary>
    private static bool IsRateLimitedCode(string message)
        => message.StartsWith("SMS_RESEND_TOO_FREQUENT", StringComparison.Ordinal)
        || message.StartsWith("SMS_HOURLY_LIMIT", StringComparison.Ordinal)
        || message.StartsWith("SMS_DAILY_LIMIT", StringComparison.Ordinal)
        || message.StartsWith("SMS_VERIFY_ATTEMPT_LIMIT", StringComparison.Ordinal)
        || message.StartsWith("TICKET_RATE_LIMITED", StringComparison.Ordinal);   // V0.10.0 票据 SPA 兑现频控

    /// <summary>领域架构守卫异常判定（T5 2026-10-09）——<c>User.Use&lt;T&gt;()</c> 帧外/未装配守卫工厂
    /// （AddConstructibleService 实现类 throw-factory / 接口守卫工厂）抛 <c>InvalidOperationException</c> + "领域架构守卫" 前缀。
    /// 外部 IdP 登录 fail-hard：Federation 未装配 → 守卫异常 → 503 EXTERNAL_IDP_NOT_CONFIGURED。</summary>
    private static bool IsDomainGuardException(InvalidOperationException ex)
        => ex.Message.Contains("领域架构守卫", StringComparison.Ordinal);

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

/// <summary>令牌响应（标准形态——access_token/refresh_token/token_type/expires_in）。
/// ⚠️ V0.10.1 双发收口：文档契约键（snake_case）自 V0.6.0 起即如此约定、实现此前仅 camelCase——现补 4 个
/// snake_case 别名属性双键并存，两类消费方零破坏；同传同名冲突不存在（响应只读）。</summary>
/// <param name="AccessToken">JWT access token（RS256）。</param>
/// <param name="RefreshToken">Refresh token（rotation——SHA256 落库）。</param>
/// <param name="TokenType">固定 <c>Bearer</c>。</param>
/// <param name="ExpiresIn">access token 有效期（秒）。</param>
public sealed record TokenResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn)
{
    /// <summary>文档契约键 <c>access_token</c>（V0.6.0 起使用指南即如此约定）。</summary>
    [JsonPropertyName("access_token")]
    public string AccessTokenAlias => AccessToken;

    /// <summary>文档契约键 <c>refresh_token</c>。</summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshTokenAlias => RefreshToken;

    /// <summary>文档契约键 <c>token_type</c>。</summary>
    [JsonPropertyName("token_type")]
    public string TokenTypeAlias => TokenType;

    /// <summary>文档契约键 <c>expires_in</c>。</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresInAlias => ExpiresIn;
}

/// <summary>统一错误响应（错误码 + 可选消息）。</summary>
public sealed record ErrorResponse(string Code, string? Message);

/// <summary>短信验证码发送请求。</summary>
public sealed record SendSmsCodeRequest(string Phone, string Scene);

/// <summary>短信登录请求。</summary>
public sealed record SmsLoginRequest(string Phone, string Code);
/// <summary>密码登录请求（V0.9.0 B.9 + ADR 决策 1——SecurePassword 协议：Identifier 为 UId 或 Phone 凭据标识；
/// ClientHash/Salt 为客户端算的 PBKDF2 产物，服务端零明文）。</summary>
public sealed record PasswordLoginRequest(string Identifier, string ClientHash, string Salt);

/// <summary>刷新请求（V0.10.1 双收：文档契约 body = <c>{refresh_token}</c>；camelCase <c>refreshToken</c> 亦接受，
/// 同传时 camelCase 优先——默认 Web JSON 绑定大小写不敏感但不做下划线归一，别名键保证文档契约可用）。</summary>
public sealed record RefreshRequest(string RefreshToken)
{
    /// <summary>文档契约键 <c>refresh_token</c>。</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshTokenSnakeCase { get; init; }

    /// <summary>生效 refresh token（<c>refreshToken</c> 优先，其次 <c>refresh_token</c>）。</summary>
    [JsonIgnore]
    public string EffectiveRefreshToken
        => !string.IsNullOrWhiteSpace(RefreshToken) ? RefreshToken : RefreshTokenSnakeCase ?? string.Empty;
}

/// <summary>外部 IdP 票据 SPA 兑现请求（V0.10.0——POST {prefix}/login/ticket；channel_id 可选——多通道消歧，
/// 正常由票载荷 ChannelId 签入，提供时须一致否则 TICKET_INVALID）。</summary>
/// <param name="Ticket">32B base64url 密封票据。</param>
/// <param name="ChannelId">通道 id（键 <c>channelId</c>——minimal API 默认 Web JSON 绑定）。</param>
public sealed record ExternalTicketLoginRequest(string Ticket, string? ChannelId)
{
    /// <summary>snake_case 契约别名（使用指南 body 写作 <c>{"ticket", "channel_id"}</c>；默认绑定大小写不敏感
    /// 但不做下划线归一，<c>channel_id</c> 不会命中 <see cref="ChannelId"/>——显式映射保证文档契约可用，
    /// 同时保留 camelCase 客户端可用：两条键并存，不因客户端命名风格静默丢失消歧参数）。</summary>
    [JsonPropertyName("channel_id")]
    public string? ChannelIdSnakeCase { get; init; }

    /// <summary>生效通道 id（<c>channelId</c> 优先，其次 <c>channel_id</c>；均空 → null = 由票载荷决定）。</summary>
    [JsonIgnore]
    public string? EffectiveChannelId
        => !string.IsNullOrWhiteSpace(ChannelId) ? ChannelId : ChannelIdSnakeCase;
}

/// <summary>票据换令牌请求（PKCE code_verifier 可选——IssueAsync 时若生成则 Exchange 必传；V0.10.1 双收：
/// 文档契约 body = <c>{ticket, code_verifier, app_id, state}</c>；camelCase <c>codeVerifier</c>/<c>appId</c> 亦接受，
/// 同传时 camelCase 优先——别名键保证文档契约可用）。</summary>
public sealed record TicketExchangeRequest(string Ticket, string? CodeVerifier, string AppId, string? State)
{
    /// <summary>文档契约键 <c>code_verifier</c>。</summary>
    [JsonPropertyName("code_verifier")]
    public string? CodeVerifierSnakeCase { get; init; }

    /// <summary>文档契约键 <c>app_id</c>。</summary>
    [JsonPropertyName("app_id")]
    public string? AppIdSnakeCase { get; init; }

    /// <summary>生效 code_verifier（<c>codeVerifier</c> 优先，其次 <c>code_verifier</c>）。</summary>
    [JsonIgnore]
    public string? EffectiveCodeVerifier
        => !string.IsNullOrWhiteSpace(CodeVerifier) ? CodeVerifier : CodeVerifierSnakeCase;

    /// <summary>生效 app id（<c>appId</c> 优先，其次 <c>app_id</c>）。</summary>
    [JsonIgnore]
    public string EffectiveAppId
        => !string.IsNullOrWhiteSpace(AppId) ? AppId : AppIdSnakeCase ?? string.Empty;
}

// ─────────────────────────── V0.8.0 认证 API 补全（Web DTO） ───────────────────────────

/// <summary>令牌验签快照（/verify——已认证内省，非 RFC 7662 fanout；恒 200 于到达后，无效 Bearer → 401）。</summary>
/// <param name="Valid">恒 true（到达即已验签）。</param>
/// <param name="UserId">平台内部 id（JWT sub 同源）。</param>
/// <param name="AuthType">认证方式（sms/federated/password/redeem）。</param>
/// <param name="AuthLevel">认证强度。</param>
/// <param name="Exp">过期时间（unix 秒）。</param>
/// <param name="Jti">令牌唯一 id。</param>
public sealed record VerifyResponse(
    bool Valid,
    string UserId,
    string AuthType,
    [property: System.Text.Json.Serialization.JsonPropertyName("auth_level")] int AuthLevel,  // P1-2：Results.Ok 默认 camelCase（authLevel）与 README snake_case（auth_level）对齐——局部显式指定
    long Exp,
    string Jti);

/// <summary>应用授权视图（/grants 列表项——Scopes 已拆为列表，拆分归 Web 层 Oracle P2-2 部分采纳）。</summary>
/// <param name="AppId">授权应用 id。</param>
/// <param name="Scopes">授权范围（逗号串拆分）。</param>
/// <param name="ValidUntil">应用授权有效期（null = 持续至吊销——非会话有效期）。</param>
/// <param name="Source">授权来源（AuthGrantSources：login/redeem）。</param>
/// <param name="Status">授权状态（0 Active / 1 Revoked）。</param>
public sealed record GrantView(
    string AppId,
    IReadOnlyList<string> Scopes,
    DateTime? ValidUntil,
    string Source,
    int Status);

/// <summary>应用授权状态响应（/grants）。</summary>
/// <param name="Grants">有效授权列表（app_id 可空 = 全部）。</param>
public sealed record GrantsResponse(IReadOnlyList<GrantView> Grants);

/// <summary>验证码独立校验请求（/sms/verify——匿名端点）。</summary>
/// <param name="Phone">目标手机号。</param>
/// <param name="Code">验证码。</param>
/// <param name="Scene">场景（SmsScenes 白名单：Login/Register/Bind/Reset）。</param>
public sealed record SmsVerifyRequest(string Phone, string Code, string Scene);

/// <summary>验证码独立校验响应（/sms/verify——不返回令牌，独立校验非登录）。</summary>
/// <param name="Verified">是否校验通过（单次消费语义）。</param>
public sealed record SmsVerifyResponse(bool Verified);
