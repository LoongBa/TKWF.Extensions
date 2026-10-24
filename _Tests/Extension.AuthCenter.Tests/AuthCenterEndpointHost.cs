using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Web.Extensions;
using TKW.Framework.Domain.Web.Hosting;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// 测试项目共享 Web 宿主（V0.6.0 归层迭代 T5）——<c>DomainHost&lt;TUserInfo&gt;.Root</c> 进程内单例
/// （"DomainHost 不能重复初始化"），全测试项目**仅可建 1 个 Web 宿主**——JwtAuth 顺序冒烟
/// （JwtAuthenticationWebHookIntegrationTests）+ AuthCenterWebExtension 端点冒烟（AuthCenterWebExtensionTests）共用。
/// <para>含：哨兵扩展（ContextExtractionProbe/JwtAuthVerifyProbe——顺序断言）+ <see cref="AuthCenterWebExtension{TUserInfo}"/>
/// （端点映射）+ Fake 门面（端点行为断言——普通 Scoped 经 RegisterServices 替换守卫工厂）+ FakeTokenVerifier（Bearer 验签）。
/// 宿主参数对齐 JwtAuth 冒烟先例：Development（ValidateScopes=true 回归哨兵）+ TKWDomain:Name + UseNoEntityDAC + ExcludedPathPrefixes /api。</para>
/// </summary>
internal static class AuthCenterEndpointHost
{
    public const string OrderKey = "__order";
    public const string RestoredUserId = "jwt-user-100";
    private const string DomainUserItemsKey = "DomainUser";

    public static TestServer Server => _lazy.Value;
    private static readonly Lazy<TestServer> _lazy = new(CreateServer);

    // ─── Fake 门面（端点测试断言 Invoked/结果） ───
    public static FakeSmsLoginService SmsLogin { get; } = new();
    public static FakeExternalIdpLoginService ExternalLogin { get; } = new();   // T5（2026-10-09）替代 FakeWechatLoginService
    public static FakeTokenService Token { get; } = new();
    public static FakeSmsVerificationService SmsVerification { get; } = new();
    public static FakeTicketService Ticket { get; } = new();
    public static FakeAuthGrantQueryService Grants { get; } = new();        // V0.8.0 /grants 查询门面
    public static FakeAuthGrantCommandService GrantCommand { get; } = new(); // V0.8.0 /grants 写入门面（未暴露端点，注册保真）
    public static FakeExternalTicketLoginService TicketLogin { get; } = new();  // V0.10.0 /login/ticket 票据 SPA 兑现门面

    private static TestServer CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = "Development";   // ValidateScopes=true 回归哨兵
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TKWDomain:Name"] = "AuthCenterEndpointSmoke"
        });

        var app = builder.ConfigWebAppDomain<TestUserInfo, ConsumerHostInitializer, DomainWebOptions>(configure: cfg =>
        {
            cfg.UseNoEntityDAC();                               // 冒烟无数据存取（门面 Fake）
            cfg.HttpAuth.ExcludedPathPrefixes.Add("/api");      // 匿名端点免认证双保险
        })
        .RegisterServices((svc, _) =>
        {
            svc.Replace(ServiceDescriptor.Scoped<ITokenVerifier>(_ => new FakeTokenVerifier()));
            svc.Replace(ServiceDescriptor.Scoped<ISmsLoginService>(_ => SmsLogin));
            svc.Replace(ServiceDescriptor.Scoped<IExternalIdpLoginService>(_ => ExternalLogin));   // T5（2026-10-09）替代 IWechatLoginService
            svc.Replace(ServiceDescriptor.Scoped<ITokenService>(_ => Token));
            svc.Replace(ServiceDescriptor.Scoped<ISmsVerificationService>(_ => SmsVerification));
            svc.Replace(ServiceDescriptor.Scoped<IOAuthTicketService>(_ => Ticket));
            svc.Replace(ServiceDescriptor.Scoped<IAuthGrantQueryService>(_ => Grants));       // V0.8.0 /grants 查询
            svc.Replace(ServiceDescriptor.Scoped<IAuthGrantCommandService>(_ => GrantCommand)); // V0.8.0 写入门面（未暴露端点，注册保真）
            svc.Replace(ServiceDescriptor.Scoped<IExternalTicketLoginService>(_ => TicketLogin)); // V0.10.0 /login/ticket 票据兑现
        })
        .UseWebSession()
        .UseWebExtensions<TestUserInfo>(e =>
        {
            e.Add<ContextExtractionProbeWebExtension>();
            e.Add<AuthCenterWebExtension<TestUserInfo>>(x => x.RestoreUser = RestoreUser);
            e.Add<JwtAuthVerifyProbeWebExtension>();
        })
        .BeforeRouting((app, _) =>
        {
            // HttpAuthentication 标记 + 顺序串写出（黑盒观测）——⚠️ 仅 /api/test 短路（JwtAuth 顺序断言）；
            // 其余路径（端点请求 /api/auth/*）须继续走管线到 AfterRouting 端点映射
            app.Use(async (ctx, next) =>
            {
                ctx.Items[OrderKey] = (ctx.Items[OrderKey] as string ?? string.Empty) + "HttpAuthentication";
                if (ctx.Request.Path.StartsWithSegments("/api/test"))
                {
                    ctx.Response.Headers["X-Order"] = ctx.Items[OrderKey] as string ?? string.Empty;
                    ctx.Response.StatusCode = 200;
                }
                else
                {
                    await next(ctx);
                }
            });
        })
        .UseAspNetCoreRouting()
        .AfterRouting((_, _) => { })
        .Build();

        app.StartAsync().GetAwaiter().GetResult();
        return app.GetTestServer();
    }

    /// <summary>恢复委托——构建"已验签恢复"的 DomainUser（UserInfo.UserIdString 带独特标识供哨兵区分游客）。</summary>
    private static Task<DomainUser<TestUserInfo>> RestoreUser(HttpContext ctx, TokenValidationResult token)
        => Task.FromResult(new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo(RestoredUserId, "JwtTester") });

    // ─── 测试哨兵 Web 扩展（JwtAuth 顺序断言） ───

    private sealed class ContextExtractionProbeWebExtension : IWebExtension
    {
        public void ConfigureServices(IServiceCollection services) { }
        public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options)
            => app.Use(async (ctx, next) =>
            {
                if (ctx.Items[DomainUserItemsKey] != null)
                    ctx.Items[OrderKey] = (ctx.Items[OrderKey] as string ?? string.Empty) + "ContextExtraction→";
                await next(ctx);
            });
        public void ConfigureEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, DomainWebOptions options) { }
    }

    private sealed class JwtAuthVerifyProbeWebExtension : IWebExtension
    {
        public void ConfigureServices(IServiceCollection services) { }
        public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options)
            => app.Use(async (ctx, next) =>
            {
                var user = ctx.Items[DomainUserItemsKey] as DomainUser<TestUserInfo>;
                var marker = user?.UserInfo?.UserIdString == RestoredUserId ? "JwtAuth验签→" : "JwtAuth跳过→";
                ctx.Items[OrderKey] = (ctx.Items[OrderKey] as string ?? string.Empty) + marker;
                await next(ctx);
            });
        public void ConfigureEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, DomainWebOptions options) { }
    }

    // ─── Fake 门面 / 验证器 ───

    internal sealed class FakeTokenVerifier : ITokenVerifier
    {
        public Task<TokenValidationResult> VerifyAsync(string token, CancellationToken ct = default)
            => Task.FromResult(new TokenValidationResult(
                "u-100", AuthTypes.Sms, (int)AuthLevel.Phone,
                "jti-test", DateTime.UtcNow.AddHours(2),
                new Dictionary<string, string> { ["iss"] = "auth-test" }));
    }

    internal sealed class FakeSmsLoginService : ISmsLoginService
    {
        public bool Invoked { get; private set; }
        public TokenResponse Result { get; set; } = new("at-1", "rt-1", "Bearer", 7200);

        /// <summary>失败模拟（B3b——门面抛类型化异常，替代 LoginResult(false)）：非 null 时按其抛出。</summary>
        public DomainException? Throw { get; set; }

        public Task<TokenResponse> LoginAsync(SmsLoginRequest request, CancellationToken ct = default)
        {
            Invoked = true;
            if (Throw is not null) throw Throw;
            // 空参数校验模拟（保留原 handler 400 INVALID_ARGUMENT 黑盒语义——Body 绑定后门面校验）
            if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Code))
                throw new DomainException("INVALID_ARGUMENT") { ErrorCode = "INVALID_ARGUMENT" };
            return Task.FromResult(Result);
        }
    }

    /// <summary>
    /// 外部 IdP 登录门面 Fake（T5 2026-10-09——替代 FakeWechatLoginService）：Invoked/LastChannelType/LastParameters
    /// 捕获 + 可配置 Result（成功 TokenResponse）/ GuardExceptionMessage（守卫异常模拟——503 EXTERNAL_IDP_NOT_CONFIGURED 冒烟）/
    /// Throw（业务失败 DomainException——400 冒烟）。
    /// </summary>
    internal sealed class FakeExternalIdpLoginService : IExternalIdpLoginService
    {
        public bool Invoked { get; private set; }
        public string? LastChannelType { get; private set; }
        public IReadOnlyDictionary<string, string?>? LastParameters { get; private set; }

        public TokenResponse Result { get; set; } = new("at-1", "rt-1", "Bearer", 7200);

        /// <summary>模拟 fail-hard 守卫异常（领域架构守卫前缀——端点映射 503 EXTERNAL_IDP_NOT_CONFIGURED）。</summary>
        public string? GuardExceptionMessage { get; set; }

        /// <summary>业务失败模拟（B3b——DomainException 400，替代 LoginResult(false)）。</summary>
        public DomainException? Throw { get; set; }

        public Task<TokenResponse> LoginAsync(string channelType, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default)
        {
            Invoked = true;
            LastChannelType = channelType;
            LastParameters = parameters;
            if (Throw is not null) throw Throw;
            if (GuardExceptionMessage is not null)
                throw new ServiceNotConfiguredException("external-idp", GuardExceptionMessage) { ErrorCode = "EXTERNAL_IDP_NOT_CONFIGURED" };
            // 缺 code 校验模拟（保留原 handler 400 INVALID_ARGUMENT 黑盒语义）
            if (!parameters.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                throw new DomainException("INVALID_ARGUMENT") { ErrorCode = "INVALID_ARGUMENT" };
            return Task.FromResult(Result);
        }
    }

    /// <summary>
    /// 票据 SPA 兑现门面 Fake（V0.10.0——POST /login/ticket）：Invoked/LastTicket/LastChannelId 捕获 +
    /// 可配置 Result（成功 TokenResponse）/ GuardExceptionMessage（守卫异常——503 TICKET_SERVICE_NOT_CONFIGURED）/
    /// RateLimitedMessage（频控异常——429 TICKET_RATE_LIMITED）/ Throw（业务失败——400）。
    /// </summary>
    internal sealed class FakeExternalTicketLoginService : IExternalTicketLoginService
    {
        public bool Invoked { get; private set; }
        public string? LastTicket { get; private set; }
        public string? LastChannelId { get; private set; }

        public TokenResponse Result { get; set; } = new("at-t", "rt-t", "Bearer", 7200);

        public string? GuardExceptionMessage { get; set; }
        public string? RateLimitedMessage { get; set; }

        /// <summary>业务失败模拟（B3b——DomainException 400，替代 LoginResult(false)）。</summary>
        public DomainException? Throw { get; set; }

        public Task<TokenResponse> LoginAsync(ExternalTicketLoginRequest request, CancellationToken ct = default)
        {
            Invoked = true;
            LastTicket = request.Ticket;
            LastChannelId = request.EffectiveChannelId;
            if (Throw is not null) throw Throw;
            if (GuardExceptionMessage is not null)
                throw new ServiceNotConfiguredException("ticket-service", GuardExceptionMessage) { ErrorCode = "TICKET_SERVICE_NOT_CONFIGURED" };
            if (RateLimitedMessage is not null)
                throw new RateLimitedException(0, "ext:ticket:test") { ErrorCode = RateLimitedMessage };
            // 缺 ticket 校验模拟（保留原 handler 400 INVALID_ARGUMENT 黑盒语义）
            if (string.IsNullOrWhiteSpace(request.Ticket))
                throw new DomainException("INVALID_ARGUMENT") { ErrorCode = "INVALID_ARGUMENT" };
            return Task.FromResult(Result);
        }
    }

    internal sealed class FakeTokenService : ITokenService
    {
        public bool RefreshInvoked { get; private set; }
        public string? LastRefreshToken { get; private set; }
        public string? RevokedJti { get; private set; }
        public bool RevokeCurrentInvoked { get; private set; }

        public Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, CancellationToken ct = default)
            => Task.FromResult(new TokenIssueResult("at-1", "rt-1", 7200));

        public Task<TokenValidationResult> ValidateTokenAsync(string accessToken, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TokenResponse> RefreshTokenAsync(RefreshRequest request, CancellationToken ct = default)
        {
            RefreshInvoked = true;
            LastRefreshToken = request.EffectiveRefreshToken;
            return Task.FromResult(new TokenResponse("at-2", "rt-2", "Bearer", 7200));
        }

        public Task RevokeTokenAsync(string jti, string reason, CancellationToken ct = default)
        {
            RevokedJti = jti;
            return Task.CompletedTask;
        }

        public Task<VerifyResponse> GetCurrentTokenInfoAsync(CancellationToken ct = default)
        {
            // B3b 黑盒语义：已认证端点（/verify）无 Bearer → 门面 ambient null → AuthenticationException("AUTH_REQUIRED")
            // （生产路径经 ITokenVerificationContext 判定；Fake 用可配置标志模拟——401 语义正确属认证异常）
            if (VerifyRequiresAuth)
                throw new System.Security.Authentication.AuthenticationException("AUTH_REQUIRED");
            return Task.FromResult(new VerifyResponse(true, "u-100", AuthTypes.Sms, (int)AuthLevel.Phone, 1234567890, "jti-test"));
        }

        public Task RevokeCurrentSessionAsync(CancellationToken ct = default)
        {
            RevokeCurrentInvoked = true;
            RevokedJti = "jti-test";   // 模拟 ambient 撤销（门面内经 ITokenVerificationContext 取当前 jti）
            return Task.CompletedTask;
        }

        /// <summary>B3b：/verify 无 Bearer 模拟（对应生产 ambient null → AUTH_REQUIRED 401）。</summary>
        public bool VerifyRequiresAuth { get; set; }
    }

    internal sealed class FakeSmsVerificationService : ISmsVerificationService
    {
        public bool SendCodeInvoked { get; private set; }

        /// <summary>重置 Invoked 标志——共享宿主跨用例状态防污染（V0.8.0 修复：负向断言测试前置重置）。</summary>
        public void Reset() => SendCodeInvoked = false;

        public Task SendCodeAsync(SendSmsCodeRequest request, CancellationToken ct = default)
        {
            // 场景白名单校验模拟（保留原 handler 400 INVALID_SCENE 黑盒语义——校验先于 Invoked 置位）
            if (!IsAllowedScene(request.Scene))
                throw new DomainException("INVALID_SCENE") { ErrorCode = "INVALID_SCENE" };
            SendCodeInvoked = true;
            return Task.CompletedTask;
        }

        public Task<SmsVerifyResponse> VerifyCodeAsync(SmsVerifyRequest request, CancellationToken ct = default)
        {
            if (!IsAllowedScene(request.Scene))
                throw new DomainException("INVALID_SCENE") { ErrorCode = "INVALID_SCENE" };
            return Task.FromResult(new SmsVerifyResponse(true));
        }

        private static bool IsAllowedScene(string scene)
            => scene == SmsScenes.Login || scene == SmsScenes.Register || scene == SmsScenes.Bind || scene == SmsScenes.Reset;
    }

    internal sealed class FakeTicketService : IOAuthTicketService
    {
        public bool ExchangeInvoked { get; private set; }
        public TicketExchangeRequest? LastRequest { get; private set; }

        public Task<string> IssueAsync(OAuthTicketIssueRequest request, CancellationToken ct = default)
            => Task.FromResult("t-1");

        public Task<TokenResponse> ExchangeAsync(TicketExchangeRequest request, CancellationToken ct = default)
        {
            ExchangeInvoked = true;
            LastRequest = request;
            return Task.FromResult(new TokenResponse("at-3", "rt-3", "Bearer", 7200));
        }

        public Task BindTicketAsync(string ticket, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>V0.8.0 /grants 查询门面 Fake——Invoked 标记 + 可配置 Result（默认 app-1 授权行）。</summary>
    internal sealed class FakeAuthGrantQueryService : IAuthGrantQueryService
    {
        public bool Invoked { get; private set; }
        public string? LastAppId { get; private set; }

        public GrantsResponse Result { get; set; } =
            new GrantsResponse(
            [
                new GrantView("app-1", ["a", "b"], null, AuthGrantSources.Login, 0)
            ]);

        /// <summary>B3b：/grants 无 Bearer 模拟（对应生产 ambient null → AUTH_REQUIRED 401）。</summary>
        public bool AuthRequired { get; set; }

        /// <summary>重置 Invoked 标志——共享宿主跨用例状态防污染（V0.8.0 修复：负向断言测试前置重置）。</summary>
        public void Reset() => Invoked = false;

        public Task<GrantsResponse> GetGrantsAsync(string? appId = null, CancellationToken ct = default)
        {
            if (AuthRequired)
                throw new System.Security.Authentication.AuthenticationException("AUTH_REQUIRED");
            Invoked = true;
            LastAppId = appId;
            return Task.FromResult(Result);
        }
    }

    /// <summary>V0.8.0 写入门面 Fake——Invoked 标记（端点未暴露，注册保真供 OAuthTicketService 生产路径替换场景）。</summary>
    internal sealed class FakeAuthGrantCommandService : IAuthGrantCommandService
    {
        public bool Invoked { get; private set; }

        public Task RecordLoginGrantAsync(string userId, string appId, CancellationToken ct = default)
        {
            Invoked = true;
            return Task.CompletedTask;
        }
    }
}
