using System;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// 限流展开内部助手（v4.10.45 收敛迁移后为 internal）——<see cref="TkfwRateLimiterOptionsSetup"/>
/// 与 <see cref="RateLimitingWebExtension"/> 共用：将 <see cref="RateLimitingOptions"/> 展开到
/// ASP.NET Core <c>RateLimiterOptions</c>（全局/端点级策略 + 分区器 + 429/Retry-After）。
/// <para>旧 <c>AddTkfwRateLimiting</c> 静态入口已删除（CHANGELOG 破坏性变更）——Web 装配收敛为
/// <see cref="RateLimitingWebExtension"/>（消费方 <c>UseWebExtensions</c> 一次声明，锚点 BeforeAuthentication）。</para>
/// <para>与 Domain 层 <c>FilterBuilder.AddRateLimit()</c> + <c>[RateLimit]</c> AOP 双层互补：
/// Web 层管 HTTP 入口 IP/端点粗粒度兜底，Domain 层管领域方法用户级细粒度——两者不替代（Oracle C2）。</para>
/// </summary>
internal static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// 将 <see cref="RateLimitingOptions"/> 展开到 ASP.NET Core <see cref="RateLimiterOptions"/>
    /// （供 ConfigureOptions Setup 与测试直接复用）。
    /// </summary>
    internal static void ApplyTo(RateLimiterOptions target, RateLimitingOptions options)
    {
        target.RejectionStatusCode = options.RejectionStatusCode;

        // OnRejected：显式写拒绝状态码（幂等）+ 可选 Retry-After 头（从 lease 元数据取）
        target.OnRejected = async (context, ct) =>
        {
            context.HttpContext.Response.StatusCode = options.RejectionStatusCode;
            if (options.RetryAfter && context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
            await Task.CompletedTask;
        };

        // 全局限流器：路径感知闭包——精确命中 EndpointPolicies 的请求用端点策略（独立配额），
        // 未命中回退 Global 策略（Oracle P2-2 精确匹配）
        target.GlobalLimiter = CreatePathAwareGlobalLimiter(options);

        // 端点策略注册为命名策略（policyName = 路径）——供 RequireRateLimiter / MapTkfwRateLimiter
        // 手动引用（自动路径感知机制与手动端点标注互补，二者不叠加）
        foreach (var (path, policy) in options.EndpointPolicies)
        {
            var resolver = ResolveKeyResolver(options);
            target.AddPolicy(path, ctx => RateLimitPartitioners.CreatePartition(resolver(ctx), policy));
        }
    }

    /// <summary>按分区配置解析 key（Ip/User/None）。</summary>
    private static Func<HttpContext, string> ResolveKeyResolver(RateLimitingOptions options)
        => options.Partition switch
        {
            RateLimitPartition.User => RateLimitPartitioners.ResolveUserKey,
            RateLimitPartition.None => _ => "global",
            _ => RateLimitPartitioners.ResolveIpKey
        };

    /// <summary>
    /// 路径感知全局限流器——自动化全局 + 端点级覆盖：
    /// 精确路径命中 <see cref="RateLimitingOptions.EndpointPolicies"/> 时使用端点策略模型，
    /// 且 key 带 <c>ep:{path}:</c> 前缀保证独立配额（不同端点策略互不串扰、与全局桶隔离）。
    /// </summary>
    private static PartitionedRateLimiter<HttpContext> CreatePathAwareGlobalLimiter(RateLimitingOptions options)
    {
        var resolver = ResolveKeyResolver(options);
        return PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (options.EndpointPolicies.TryGetValue(path, out var endpointPolicy))
                return RateLimitPartitioners.CreatePartition($"ep:{path}:{resolver(context)}", endpointPolicy);

            return RateLimitPartitioners.CreatePartition(resolver(context), options.Global);
        });
    }
}

/// <summary>
/// 延迟配置 Setup——在 RateLimiterOptions 首次解析时注入最终 <see cref="RateLimitingOptions"/>
    /// （默认值 → TKWF:RateLimiting 配置节 → RateLimitingWebExtension.ConfigureOptions 编程式，优先级递增）。
/// </summary>
internal sealed class TkfwRateLimiterOptionsSetup : IConfigureOptions<RateLimiterOptions>
{
    private readonly RateLimitingOptions _options;

    public TkfwRateLimiterOptionsSetup(IOptions<RateLimitingOptions> options)
        => _options = options.Value;

    public void Configure(RateLimiterOptions target)
        => RateLimitingServiceCollectionExtensions.ApplyTo(target, _options);
}