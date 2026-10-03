using System;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>
/// D9b：JwtAuthenticationMiddleware——Bearer JWT 请求 → HttpContext.Items[DomainUserKey] 恢复已认证 DomainUser。
/// <para>管线：JwtAuthenticationMiddleware → 断言中间件（读 Items["DomainUser"] 写响应头）→ 终点。
/// 「后续 HttpAuthenticationMiddleware 标记通过」归消费方接线（主框架 TKWF.Domain.Web 中间件——测试不引用，语义见使用指南 §二 第 3 步）。</para>
/// </summary>
public class JwtAuthenticationMiddlewareTests
{
    private const string DomainUserKey = "DomainUser"; // 对齐主框架 ContextExtractionMiddleware.DomainUserKey 常量值

    private static TestServer CreateServer(Func<HttpContext, TokenValidationResult, Task<DomainUser<TestUserInfo>>>? restoreUser = null, bool invalidToken = false)
    {
        var helper = new TestAuthUserHelper();
        // v4.10.45 收敛迁移：静态方法 AddJwtAuthentication/UseTkfwJwtAuthentication 已删除——
        // Web 装配收敛为 JwtAuthenticationWebExtension（消费方 UseWebExtensions 一次声明），
        // 测试按 WebExtension 实例化驱动（ConfigureServices + ConfigureMiddleware 手动调用）。
        var extension = new JwtAuthenticationWebExtension<TestUserInfo>
        {
            RestoreUser = (ctx, token) =>
                restoreUser != null
                    ? restoreUser(ctx, token)
                    : Task.FromResult(helper.CreateUserInstanceForTest())
        };

        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                extension.ConfigureServices(services);
                // 替换验证器：中间件行为测试用假验证器（真实验签 TokenServiceTests 已覆盖）
                if (invalidToken)
                    services.Replace(ServiceDescriptor.Scoped<ITokenVerifier>(_ => new ThrowingTokenVerifier()));
                else
                    services.Replace(ServiceDescriptor.Scoped<ITokenVerifier>(_ => new FakeTokenVerifier()));
            })
            .Configure(app =>
            {
                // JwtAuthenticationMiddleware（Bearer JWT → 恢复委托 → Items[DomainUserKey]）——BeforeAuthentication 锚点语义
                extension.ConfigureMiddleware(app, new TKW.Framework.Domain.Hosting.DomainWebOptions());
                // 断言中间件：读 Items[DomainUserKey] → 响应头（验证恢复接线；UserInfo 填充经 LoginAsUserAsync 归消费方 Host 全链路）
                app.Use((ctx, next) =>
                {
                    var user = ctx.Items[DomainUserKey] as DomainUser<TestUserInfo>;
                    if (user != null)
                        ctx.Response.Headers["X-Auth-User"] = "restored";
                    return next(ctx);
                });
                app.Run(ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });
            })
            .UseTestServer();
        return new TestServer(builder);
    }

    [Fact]
    public async Task BearerRequest_RestoresDomainUser_IntoItems()
    {
        using var server = CreateServer();
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

        var resp = await client.GetAsync("/api/test");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("restored", resp.Headers.GetValues("X-Auth-User").FirstOrDefault());
    }

    [Fact]
    public async Task NoBearer_NoDomainUserInItems()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var resp = await client.GetAsync("/api/test");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(resp.Headers.Contains("X-Auth-User"));
    }

    [Fact]
    public async Task InvalidBearer_Rejected_401()
    {
        using var server = CreateServer(invalidToken: true);
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "forged-token");

        var resp = await client.GetAsync("/api/test");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task RestoreUser_ReturnsNull_NoDomainUserInItems()
    {
        // 恢复委托返回 null（消费方判定不恢复）→ 中间件不写 Items（尊重委托结果）
        using var server = CreateServer((ctx, token) => Task.FromResult<DomainUser<TestUserInfo>>(null!));
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

        var resp = await client.GetAsync("/api/test");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(resp.Headers.Contains("X-Auth-User"));
    }

    // ── 测试桩 ───────────────────────────────────────────────────────

    private sealed class FakeTokenVerifier : ITokenVerifier
    {
        public Task<TokenValidationResult> VerifyAsync(string token, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new TokenValidationResult(
                "u-100", AuthTypes.Sms, (int)AuthLevel.Phone, false,
                "jti-test", DateTime.UtcNow.AddHours(2),
                new System.Collections.Generic.Dictionary<string, string> { ["iss"] = "auth-test" }));
    }

    private sealed class ThrowingTokenVerifier : ITokenVerifier
    {
        public Task<TokenValidationResult> VerifyAsync(string token, System.Threading.CancellationToken ct = default)
            => throw new AuthenticationException("TOKEN_MALFORMED");
    }

    private sealed class TestAuthUserHelper : AuthenticationUserHelperBase<TestUserInfo>
    {
        protected override TestUserInfo CreateUserInfoFromAccount(AuthAccountEntity account, System.Collections.Generic.IReadOnlyList<string> roles)
            => new(account.UId, account.Phone ?? account.UId) { Roles = roles.ToList() };

        protected override TestUserInfo CreateUserInfoFromToken(TokenValidationResult token, System.Collections.Generic.IReadOnlyList<string> roles)
            => new(token.UserId, token.UserId) { Roles = roles.ToList() };

        /// <summary>CreateUserInstance 经 UserHelper 子类可调（protected internal——AttachHost/LoginAsUserAsync 已认证链路归消费方 Host 全链路）。</summary>
        public DomainUser<TestUserInfo> CreateUserInstanceForTest()
            => CreateUserInstance();
    }
}
