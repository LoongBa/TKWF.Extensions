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
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthCenter.Tests;

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
/// <para>⚠️ <b>V0.5.4 回归哨兵增强</b>：宿主恒走 <b>Development</b> 环境（ValidateScopes=true）——认证
/// 启动链入口 <c>AuthCenterExtensionInitializer.InitializeAsync</c> 的 <c>BeginSystemScopeAsync</c>
/// 系统作用域正于宿主启动时执行；旧形态 <c>BeginSystemScopeAsync(sp)</c>（传 root 不建子 scope，
/// Scoped 守卫工厂从根解析）在 Development 启动必崩（EduPlatform 实证 2026-10-05）。冒烟此前恒
/// Production（ValidateScopes 关）正是该缺陷逃逸原因——Development 启动通过 = V0.5.4 修复回归护栏。</para>
/// </summary>
public class JwtAuthenticationWebHookIntegrationTests
{
    // ═══════ V0.6.0 归层迭代（T5）改造：DomainHost<TUserInfo>.Root 进程内单例——全测试项目仅可建
    // 1 个 Web 宿主——JwtAuth 顺序冒烟 + AuthCenterWebExtension 端点冒烟共用 AuthCenterEndpointHost（共享宿主）
    // （含哨兵扩展 + AuthCenterWebExtension + FakeVerifier + Fake 门面；顺序断言逻辑不变）═══════

    // ─── 黑盒哨兵：顺序断言 ───────────────────────────────────────────

    [Fact]
    public async Task FullChain_OrderString_ContextExtraction_JwtAuthVerify_HttpAuthentication()
    {
        // 共享宿主（AuthCenterEndpointHost——进程内单例，含哨兵 + AuthCenterWebExtension）
        var server = AuthCenterEndpointHost.Server;

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
}
