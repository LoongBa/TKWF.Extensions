using System.Net;
using System.Net.Http.Headers;
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

namespace TKWF.Ext.Authentication.Tests;

/// <summary>
/// v4.10.45 Web 装配钩子（ADR87/D22/G18）集成冒烟——黑盒哨兵中间件法：
/// TestServer 内经消费方全链（ConfigWebAppDomain → UseWebSession → UseWebExtensions）装配
/// <see cref="JwtAuthenticationWebExtension{TUserInfo}"/>，哨兵 Web 扩展在 ConfigureMiddleware 注入
/// 记录 <c>HttpContext.Items["__order"]</c> 的中间件，发请求断言顺序字符串
/// <b>ContextExtraction → JwtAuth 验签 → HttpAuthentication</b>。
/// <para><b>核心验收（P0-1 锚点分桶设计目标）</b>：JwtAuth 验签必须在框架认证判定（HttpAuthentication）
/// 之前——锚点桶在 ContextExtraction 之后、HttpAuthentication 之前排空。</para>
/// <para>黑盒观测：<see cref="ContextExtractionProbeWebExtension"/> 断言 ContextExtraction 阶段 2
/// （UseWebSession 会话解析）已写游客 DomainUser；<see cref="JwtAuthVerifyProbeWebExtension"/> 断言
/// JwtAuth 中间件真实执行验签 + 恢复（Items 中用户 UserId 变为恢复委托产物）；HttpAuthentication 标记
/// 经消费方 <c>BeforeRouting</c> 回调（管道序在 HttpAuthentication 之后）追加。</para>
/// </summary>
public class JwtAuthenticationWebHookIntegrationTests
{
    private const string OrderKey = "__order";
    private const string RestoredUserId = "jwt-user-100";

    // ─── 黑盒哨兵：顺序断言 ───────────────────────────────────────────

    [Fact]
    public async Task FullChain_OrderString_ContextExtraction_JwtAuthVerify_HttpAuthentication()
    {
        // 单一宿主（DomainHost<TUserInfo>.Root 静态——进程内不可重复初始化，两场景共用同一 TestServer）
        using var server = CreateServer();

        // 场景 1（正向）：Bearer JWT → JwtAuth 真实验签 + 恢复 →
        // 顺序串 = ContextExtraction → JwtAuth 验签 → HttpAuthentication（核心验收 P0-1 锚点分桶）
        var client1 = server.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");
        var resp1 = await client1.GetAsync("/api/test");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal("ContextExtraction→JwtAuth验签→HttpAuthentication", GetOrderHeader(resp1));

        // 场景 2（负向控制）：无 Bearer → JwtAuth 中间件透传（不验签）——哨兵应记录 "JwtAuth跳过"
        // （证明 "JwtAuth验签" 标记确实由 JWT 处理触发，非恒真；每次请求独立 HttpContext.Items）
        var client2 = server.CreateClient();
        var resp2 = await client2.GetAsync("/api/test");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("ContextExtraction→JwtAuth跳过→HttpAuthentication", GetOrderHeader(resp2));
    }

    private static string? GetOrderHeader(HttpResponseMessage resp)
        => resp.Headers.TryGetValues("X-Order", out var values) ? values.FirstOrDefault() : null;

    // ─── 全链宿主（消费方真实形态）────────────────────────────────────

    private static TestServer CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        // ConfigWebAppDomain 要求 TKWDomain 配置节存在（CoreConfigDomain 硬校验）——注入最小节
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TKWDomain:Name"] = "JwtAuthSmokeTest"
        });

        // 消费方全链：ConfigWebAppDomain → RegisterServices → UseWebSession → UseWebExtensions → BeforeRouting → Build
        var app = builder.ConfigWebAppDomain<TestUserInfo, ConsumerHostInitializer, DomainWebOptions>(configure: cfg =>
        {
            cfg.UseNoEntityDAC();   // 冒烟测试无数据存取需求（AddDomain EntityDAC 门控——显式跳过）
            // 冒烟测试 /api 路径免认证——HttpAuthentication 透传未认证游客，顺序断言不受 401 短路影响
            cfg.HttpAuth.ExcludedPathPrefixes.Add("/api");
        })
        .RegisterServices((svc, _) =>
        {
            // 替换验证器：真实验签（TokenService/密钥）由 TokenServiceTests 覆盖——此处聚焦装配顺序
            svc.Replace(ServiceDescriptor.Scoped<ITokenVerifier>(_ => new FakeTokenVerifier()));
        })
        .UseWebSession()
        .UseWebExtensions<TestUserInfo>(e =>
        {
            // 注册序 = 管道序：① 上下文提取哨兵 → ② JwtAuth 扩展（锚点默认 BeforeAuthentication）→ ③ 验签哨兵
            e.Add<ContextExtractionProbeWebExtension>();
            e.Add<JwtAuthenticationWebExtension<TestUserInfo>>(x => x.RestoreUser = RestoreUser);
            e.Add<JwtAuthVerifyProbeWebExtension>();
        })
        .BeforeRouting((app, _) =>
        {
            // 消费方 BeforeRouting 回调在 HttpAuthentication 之后执行（D22 §六现状语义）——
            // 此处追加 HttpAuthentication 标记 + 终结点写出顺序串（黑盒观测；终结点短路——UseEndpoints 不触发）
            app.Use(async (ctx, next) =>
            {
                ctx.Items[OrderKey] = (ctx.Items[OrderKey] as string ?? string.Empty) + "HttpAuthentication";
                await next(ctx);
            });
            app.Run(ctx =>
            {
                ctx.Response.Headers["X-Order"] = ctx.Items[OrderKey] as string ?? string.Empty;
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            });
        })
        .UseAspNetCoreRouting()
        .AfterRouting((_, _) => { })   // 消费方标准形态：显式开启路由阶段（端点映射无实际需求——终结点已短路）
        .Build();

        app.StartAsync().GetAwaiter().GetResult();
        return app.GetTestServer();
    }

    /// <summary>恢复委托——构建"已验签恢复"的 DomainUser（UserInfo.UserIdString 带独特标识供哨兵区分游客）。</summary>
    private static Task<DomainUser<TestUserInfo>> RestoreUser(HttpContext ctx, TokenValidationResult token)
        => Task.FromResult(new DomainUser<TestUserInfo>
        {
            UserInfo = new TestUserInfo(RestoredUserId, "JwtTester")
        });

    // ─── 测试哨兵 Web 扩展 ───────────────────────────────────────────

    /// <summary>上下文提取哨兵（桶内最先执行）：ContextExtraction 阶段 2 已写游客 DomainUser → 追加 "ContextExtraction→" 标记。</summary>
    private sealed class ContextExtractionProbeWebExtension : IWebExtension
    {
        public void ConfigureServices(IServiceCollection services) { }
        public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options)
            => app.Use(async (ctx, next) =>
            {
                if (ctx.Items[ContextExtractionMiddlewareKey] != null)
                    ctx.Items[OrderKey] = (ctx.Items[OrderKey] as string ?? string.Empty) + "ContextExtraction→";
                await next(ctx);
            });
        public void ConfigureEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, DomainWebOptions options) { }
    }

    /// <summary>验签哨兵（桶内 JwtAuth 之后）：Items 用户 UserId == 恢复委托产物 → 追加 "JwtAuth验签→"；否则 "JwtAuth跳过→"。</summary>
    private sealed class JwtAuthVerifyProbeWebExtension : IWebExtension
    {
        public void ConfigureServices(IServiceCollection services) { }
        public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options)
            => app.Use(async (ctx, next) =>
            {
                var user = ctx.Items[ContextExtractionMiddlewareKey] as DomainUser<TestUserInfo>;
                var marker = user?.UserInfo?.UserIdString == RestoredUserId ? "JwtAuth验签→" : "JwtAuth跳过→";
                ctx.Items[OrderKey] = (ctx.Items[OrderKey] as string ?? string.Empty) + marker;
                await next(ctx);
            });
        public void ConfigureEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints, DomainWebOptions options) { }
    }

    /// <summary>对齐主框架 ContextExtractionMiddleware.DomainUserKey 常量值（"DomainUser"）。</summary>
    private const string ContextExtractionMiddlewareKey = "DomainUser";

    // ─── 假验证器（对齐 JwtAuthenticationMiddlewareTests）────────────────

    private sealed class FakeTokenVerifier : ITokenVerifier
    {
        public Task<TokenValidationResult> VerifyAsync(string token, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new TokenValidationResult(
                "u-100", AuthTypes.Sms, (int)AuthLevel.Phone, false,
                "jti-test", System.DateTime.UtcNow.AddHours(2),
                new System.Collections.Generic.Dictionary<string, string> { ["iss"] = "auth-test" }));
    }
}
