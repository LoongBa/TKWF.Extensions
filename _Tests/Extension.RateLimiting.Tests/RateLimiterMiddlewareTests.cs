using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using TKWF.Ext.RateLimiting;

namespace TKWF.Ext.RateLimiting.Tests;

/// <summary>
/// HTTP 级限流中间件测试——TestServer 起端点，验证验收标准 D2-D8：
/// 固定窗口 / 滑动窗口 / 令牌桶 / 端点级覆盖 / IP 隔离 / 用户分区（匿名 fallback IP）/ 可配 429/Retry-After / Options 绑定。
/// </summary>
public class RateLimiterMiddlewareTests
{
    // ── D2 固定窗口：IP 分区，N 次内放行，超限 429 + Retry-After ─────────────────────────────

    [Fact]
    public async Task FixedWindow_AllowsUpToLimit_ThenRejects429_WithRetryAfter()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 3, WindowSeconds = 60 };
            o.Partition = RateLimitPartition.Ip;
        });
        using var client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);

        // 前 3 次放行
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);

        // 第 4 次拒绝 429 + Retry-After
        var rejected = await client.GetAsync("/api/test");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.NotNull(rejected.Headers.RetryAfter?.Delta);
        Assert.True(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds >= 1);
    }

    // ── D3 滑动窗口：分段窗口滑动——窗口过期后许可回收 ──────────────────────────────────────

    [Fact]
    public async Task SlidingWindow_SegmentsReclaim_AfterWindowElapsed()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel
            {
                Algorithm = RateLimitAlgorithm.SlidingWindow,
                PermitLimit = 2, WindowSeconds = 1, SegmentsPerWindow = 2
            };
            o.Partition = RateLimitPartition.Ip;
        });
        using var client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/test")).StatusCode);

        // 整个 1s 窗口过期 → 分段回收，许可恢复
        await Task.Delay(1100);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
    }

    // ── D4 令牌桶：突发许可 + 按速率补充 ─────────────────────────────────────────────────────

    [Fact]
    public async Task TokenBucket_BurstAllowed_ThenReplenishesAtRate()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel
            {
                Algorithm = RateLimitAlgorithm.TokenBucket,
                PermitLimit = 2,       // TokenLimit 最大容量（突发许可，Oracle P2-1）
                ReplenishmentTokensPerSecond = 1,
                QueueLimit = 0
            };
            o.Partition = RateLimitPartition.Ip;
        });
        using var client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);

        // 突发 2 次许可
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        // 桶空——立即拒绝（QueueLimit=0）
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/test")).StatusCode);

        // 1s 补充 1 token → 放行 1 次后再拒绝
        await Task.Delay(1300);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/test")).StatusCode);
    }

    // ── D5 端点级覆盖（精确路径匹配，Oracle P2-2）：/api/auth/login 更严 + 未命中回退全局 ────

    [Fact]
    public async Task EndpointPolicy_OverridesGlobal_ForExactPath_OthersFallBackToGlobal()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 100, WindowSeconds = 60 };
            o.Partition = RateLimitPartition.Ip;
            o.EndpointPolicies["/api/auth/login"] = new RateLimitPolicyModel
            {
                Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 2, WindowSeconds = 60
            };
        });
        using var client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);

        // /api/auth/login：2 次放行、第 3 次拒绝（端点级更严生效）
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/login", new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/login", new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/auth/login", new StringContent("{}"))).StatusCode);

        // 其它路径未命中端点策略 → 回退全局（100 内放行；同 IP 端点配额已耗尽互不影响）
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/public")).StatusCode);
    }

    // ── D5 分区隔离：不同 IP 互不影响 ────────────────────────────────────────────────────────

    [Fact]
    public async Task FixedWindow_DifferentIps_AreIsolated()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 1, WindowSeconds = 60 };
            o.Partition = RateLimitPartition.Ip;
        });
        using var clientA = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);
        using var clientB = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.B);

        // IP-A 用尽配额
        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await clientA.GetAsync("/api/test")).StatusCode);

        // IP-B 独立配额
        Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await clientB.GetAsync("/api/test")).StatusCode);
    }

    // ── D6 用户分区：已认证用户按用户 ID 独立配额（Oracle C1 HttpContext.User 解析）─────────

    [Fact]
    public async Task UserPartition_AuthenticatedUsers_HaveIndependentQuota()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 1, WindowSeconds = 60 };
            o.Partition = RateLimitPartition.User;
        });
        using var user1Client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A, "user-1");
        using var user2Client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A, "user-2");
        // user-1 换 IP 仍计入同一用户配额（用户 key 与 IP 无关）
        using var user1ClientOtherIp = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.B, "user-1");

        // user-1：配额 1 次（换 IP 仍共享用户配额）
        Assert.Equal(HttpStatusCode.OK, (await user1Client.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await user1ClientOtherIp.GetAsync("/api/test")).StatusCode);

        // user-2：独立配额（不同用户互不影响）
        Assert.Equal(HttpStatusCode.OK, (await user2Client.GetAsync("/api/test")).StatusCode);
    }

    // ── C1 / D6 匿名 fallback IP：未认证请求按 IP 分区 ───────────────────────────────────────

    [Fact]
    public async Task UserPartition_Anonymous_FallsBackToIpPartition()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 1, WindowSeconds = 60 };
            o.Partition = RateLimitPartition.User;
        });
        using var anonA = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);
        using var anonB = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.B);
        using var user1 = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A, "user-1");

        // 匿名 + IP-A：按 IP 分区
        Assert.Equal(HttpStatusCode.OK, (await anonA.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await anonA.GetAsync("/api/test")).StatusCode);

        // 匿名 + IP-B：独立配额
        Assert.Equal(HttpStatusCode.OK, (await anonB.GetAsync("/api/test")).StatusCode);

        // 已认证 user-1 走用户分区（与匿名 IP 配额互不干扰）
        Assert.Equal(HttpStatusCode.OK, (await user1.GetAsync("/api/test")).StatusCode);
    }

    // ── D7 RejectionStatusCode / RetryAfter 可配置 ───────────────────────────────────────────

    [Fact]
    public async Task RejectionStatusCode_IsConfigurable()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 1, WindowSeconds = 60 };
            o.RejectionStatusCode = 401;
        });
        using var client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/test")).StatusCode);
    }

    [Fact]
    public async Task RetryAfter_Disabled_NoHeaderOnRejection()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 1, WindowSeconds = 60 };
            o.RetryAfter = false;
        });
        using var client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        var rejected = await client.GetAsync("/api/test");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Null(rejected.Headers.RetryAfter);
    }

    // ── 分区 None：全局限流器共享同一配额 ────────────────────────────────────────────────────

    [Fact]
    public async Task PartitionNone_GlobalSharedQuota_AcrossIps()
    {
        using var suite = RateLimitingTestInfra.BuildHost(o =>
        {
            o.Global = new RateLimitPolicyModel { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 1, WindowSeconds = 60 };
            o.Partition = RateLimitPartition.None;
        });
        using var clientA = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);
        using var clientB = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.B);

        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await clientB.GetAsync("/api/test")).StatusCode);
    }

    // ── D8 Options 绑定：配置节驱动中间件（无编程式 configure）───────────────────────────────

    [Fact]
    public async Task Options_Binding_FromConfiguration_DrivesMiddleware()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TKWF:RateLimiting:Global:Algorithm"] = "FixedWindow",
                ["TKWF:RateLimiting:Global:PermitLimit"] = "2",
                ["TKWF:RateLimiting:Global:WindowSeconds"] = "60",
                ["TKWF:RateLimiting:Partition"] = "Ip",
                ["TKWF:RateLimiting:RejectionStatusCode"] = "429"
            })
            .Build();

        using var suite = RateLimitingTestInfra.BuildHost(configure: null, configuration: configuration);
        using var client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/test")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/test")).StatusCode);
    }

    // ── 标注式端点策略：MapTkfwRateLimiter 挂命名策略（AddPolicy 注册）──────────────────────

    [Fact]
    public async Task MapTkfwRateLimiter_AttachesNamedPolicy_ToEndpoint()
    {
        using var suite = RateLimitingTestInfra.BuildHost(
            configure: o =>
            {
                o.Global = new RateLimitPolicyModel { Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 100, WindowSeconds = 60 };
                o.Partition = RateLimitPartition.Ip;
                o.EndpointPolicies["/api/auth/login"] = new RateLimitPolicyModel
                {
                    Algorithm = RateLimitAlgorithm.FixedWindow, PermitLimit = 2, WindowSeconds = 60
                };
            },
            mapEndpoints: a =>
            {
                a.MapPost("/api/auth/login", () => Results.Ok("login"))
                    .MapTkfwRateLimiter("/api/auth/login");
            });
        using var client = RateLimitingTestInfra.CreateClient(suite.Server, TestIps.A);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/login", new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/login", new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/auth/login", new StringContent("{}"))).StatusCode);
    }
}