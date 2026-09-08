using System;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// 分区器工厂——IP（RemoteIpAddress）与 User（HttpContext.User ClaimsPrincipal 解析）分区。
/// <para>Oracle C1：用户分区器从 <see cref="HttpContext.User"/>（ClaimsPrincipal）解析——Web 中间件
/// 管线早于 Domain 层，DomainUser/ISessionManager 在此不可用；匿名请求 fallback IP 分区。</para>
/// </summary>
public static class RateLimitPartitioners
{
    /// <summary>
    /// 构建 IP 分区限流器（key = <see cref="ConnectionInfo.RemoteIpAddress"/>）。
    /// </summary>
    /// <param name="policy">策略模型（算法 + 参数）。</param>
    public static PartitionedRateLimiter<HttpContext> CreateIpLimiter(RateLimitPolicyModel policy)
        => PartitionedRateLimiter.Create<HttpContext, string>(context =>
            CreatePartition(ResolveIpKey(context), policy));

    /// <summary>
    /// 构建用户分区限流器（key = HttpContext.User 的 NameIdentifier（优先）/ Name；
    /// 匿名请求 fallback IP 分区）。
    /// </summary>
    /// <param name="policy">策略模型（算法 + 参数）。</param>
    public static PartitionedRateLimiter<HttpContext> CreateUserLimiter(RateLimitPolicyModel policy)
        => PartitionedRateLimiter.Create<HttpContext, string>(context =>
            CreatePartition(ResolveUserKey(context), policy));

    /// <summary>
    /// 解析用户分区 key——ClaimsPrincipal 的 NameIdentifier 优先、回退 Name；
    /// 均不存在（匿名）时 fallback 客户端 IP。
    /// </summary>
    public static string ResolveUserKey(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(id))
                return $"user:{id}";

            var name = user.FindFirstValue(ClaimTypes.Name);
            if (!string.IsNullOrEmpty(name))
                return $"user:{name}";
        }

        return ResolveIpKey(context);
    }

    /// <summary>
    /// 解析 IP 分区 key——<see cref="ConnectionInfo.RemoteIpAddress"/>；
    /// 空 IP（TestServer/内联请求上下文）fallback 固定占位键。
    /// </summary>
    public static string ResolveIpKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        return ip != null ? $"ip:{ip}" : "ip:unknown";
    }

    /// <summary>
    /// 按策略模型创建 <see cref="RateLimitPartition{TKey}"/>（算法分发）。
    /// 令牌桶（Oracle P2-1）：PermitLimit = TokenLimit 最大容量，补充 = TokensPerPeriod /
    /// ReplenishmentPeriod（每 1 秒 1 周期）。
    /// </summary>
    internal static System.Threading.RateLimiting.RateLimitPartition<string> CreatePartition(string key, RateLimitPolicyModel policy)
    {
        if (policy == null)
            throw new ArgumentNullException(nameof(policy));

        return policy.Algorithm switch
        {
            RateLimitAlgorithm.FixedWindow => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(key,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = policy.PermitLimit,
                    Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                    QueueLimit = policy.QueueLimit,
                    AutoReplenishment = true
                }),

            RateLimitAlgorithm.SlidingWindow => System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(key,
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = policy.PermitLimit,
                    Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                    SegmentsPerWindow = policy.SegmentsPerWindow,
                    QueueLimit = policy.QueueLimit,
                    AutoReplenishment = true
                }),

            RateLimitAlgorithm.TokenBucket => System.Threading.RateLimiting.RateLimitPartition.GetTokenBucketLimiter(key,
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = policy.PermitLimit,
                    TokensPerPeriod = Math.Max(1, policy.ReplenishmentTokensPerSecond),
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = policy.QueueLimit,
                    AutoReplenishment = true
                }),

            _ => throw new ArgumentOutOfRangeException(nameof(policy.Algorithm), policy.Algorithm, "未知限流算法。")
        };
    }
}