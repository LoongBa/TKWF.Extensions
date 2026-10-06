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
    public static FakeWechatLoginService WechatLogin { get; } = new();
    public static FakeTokenService Token { get; } = new();
    public static FakeSmsVerificationService SmsVerification { get; } = new();
    public static FakeTicketService Ticket { get; } = new();
    public static FakeAuthGrantQueryService Grants { get; } = new();        // V0.8.0 /grants 查询门面
    public static FakeAuthGrantCommandService GrantCommand { get; } = new(); // V0.8.0 /grants 写入门面（未暴露端点，注册保真）

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
            svc.Replace(ServiceDescriptor.Scoped<IWechatLoginService>(_ => WechatLogin));
            svc.Replace(ServiceDescriptor.Scoped<ITokenService>(_ => Token));
            svc.Replace(ServiceDescriptor.Scoped<ISmsVerificationService>(_ => SmsVerification));
            svc.Replace(ServiceDescriptor.Scoped<IOAuthTicketService>(_ => Ticket));
            svc.Replace(ServiceDescriptor.Scoped<IAuthGrantQueryService>(_ => Grants));       // V0.8.0 /grants 查询
            svc.Replace(ServiceDescriptor.Scoped<IAuthGrantCommandService>(_ => GrantCommand)); // V0.8.0 写入门面（未暴露端点，注册保真）
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
                "u-100", AuthTypes.Sms, (int)AuthLevel.Phone, false,
                "jti-test", DateTime.UtcNow.AddHours(2),
                new Dictionary<string, string> { ["iss"] = "auth-test" }));
    }

    internal sealed class FakeSmsLoginService : ISmsLoginService
    {
        public bool Invoked { get; private set; }
        public LoginResult Result { get; set; } =
            new(true, "u_1", new TokenIssueResult("at-1", "rt-1", 7200), null);

        public Task<LoginResult> LoginAsync(string phone, string code, CancellationToken ct = default)
        {
            Invoked = true;
            return Task.FromResult(Result);
        }
    }

    internal sealed class FakeWechatLoginService : IWechatLoginService
    {
        public bool Invoked { get; private set; }

        public Task<LoginResult> LoginAsync(string wechatCode, string wechatScope, CancellationToken ct = default)
        {
            Invoked = true;
            return Task.FromResult(new LoginResult(true, "u_1", new TokenIssueResult("at-1", "rt-1", 7200), null));
        }
    }

    internal sealed class FakeTokenService : ITokenService
    {
        public bool RefreshInvoked { get; private set; }
        public string? RevokedJti { get; private set; }

        public Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, CancellationToken ct = default)
            => Task.FromResult(new TokenIssueResult("at-1", "rt-1", 7200));

        public Task<TokenValidationResult> ValidateTokenAsync(string accessToken, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TokenRefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
        {
            RefreshInvoked = true;
            return Task.FromResult(new TokenRefreshResult("at-2", "rt-2", 7200));
        }

        public Task RevokeTokenAsync(string jti, string reason, CancellationToken ct = default)
        {
            RevokedJti = jti;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeSmsVerificationService : ISmsVerificationService
    {
        public bool SendCodeInvoked { get; private set; }

        /// <summary>重置 Invoked 标志——共享宿主跨用例状态防污染（V0.8.0 修复：负向断言测试前置重置）。</summary>
        public void Reset() => SendCodeInvoked = false;

        public Task SendCodeAsync(string phone, string scene, CancellationToken ct = default)
        {
            SendCodeInvoked = true;
            return Task.CompletedTask;
        }

        public Task<bool> VerifyCodeAsync(string phone, string code, string scene, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    internal sealed class FakeTicketService : IOAuthTicketService
    {
        public bool ExchangeInvoked { get; private set; }

        public Task<string> IssueAsync(OAuthTicketIssueRequest request, CancellationToken ct = default)
            => Task.FromResult("t-1");

        public Task<OAuthTicketExchangeResult> ExchangeAsync(OAuthTicketExchangeRequest request, CancellationToken ct = default)
        {
            ExchangeInvoked = true;
            return Task.FromResult(new OAuthTicketExchangeResult("at-3", "rt-3", 7200));
        }

        public Task BindTicketAsync(string ticket, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>V0.8.0 /grants 查询门面 Fake——Invoked 标记 + 可配置 Result（默认 app-1 授权行）。</summary>
    internal sealed class FakeAuthGrantQueryService : IAuthGrantQueryService
    {
        public bool Invoked { get; private set; }

        public IReadOnlyList<AuthGrantEntity> Result { get; set; } =
        [
            new AuthGrantEntity
            {
                UserId = "u-100",
                AppId = "app-1",
                Scopes = "a,b",
                ValidUntil = null,
                Source = AuthGrantSources.Login,
                Status = 0
            }
        ];

        /// <summary>重置 Invoked 标志——共享宿主跨用例状态防污染（V0.8.0 修复：负向断言测试前置重置）。</summary>
        public void Reset() => Invoked = false;

        public Task<IReadOnlyList<AuthGrantEntity>> GetGrantsAsync(string userId, string? appId = null, CancellationToken ct = default)
        {
            Invoked = true;
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
