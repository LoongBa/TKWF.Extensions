using System;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// 接线入口——一键展开 ASP.NET Core 内置 <c>AddRateLimiter</c> 中间件
/// （net10 内置 System.Threading.RateLimiting，零第三方依赖）：
/// <list type="bullet">
/// <item><b>全局策略</b>：Options.Global 兜底所有端点；<b>端点级覆盖</b>：EndpointPolicies
///   按<b>精确路径</b>匹配（Oracle P2-2）自动生效（未命中回退全局；命中独立配额）</item>
/// <item><b>分区器</b>：Ip（RemoteIpAddress）/ User（HttpContext.User ClaimsPrincipal，匿名 fallback IP，Oracle C1）</item>
/// <item><b>响应语义</b>：RejectionStatusCode（默认 429，对齐 Domain RateLimitException）+ Retry-After 头（可配）</item>
/// <item><b>Options 绑定</b>：TKWF:RateLimiting 配置节（AddOptions().BindConfiguration——与 Initializer 幂等）；
///   编程式 configure 回调优先覆盖配置节</item>
/// </list>
/// <para>与 Domain 层 <c>FilterBuilder.AddRateLimit()</c> + <c>[RateLimit]</c> AOP 双层互补：
/// Web 层管 HTTP 入口 IP/端点粗粒度兜底，Domain 层管领域方法用户级细粒度——两者不替代（Oracle C2）。</para>
/// </summary>
public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Web 层限流（AddRateLimiter 展开 + Options 绑定 + 分区器 + 429/Retry-After）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">编程式配置（可选）——覆盖 <c>TKWF:RateLimiting</c> 配置节后应用（编程优先）。</param>
    public static IServiceCollection AddTkfwRateLimiting(
        this IServiceCollection services,
        Action<RateLimitingOptions>? configure = null)
    {
        // ① Options 绑定：配置节 TKWF:RateLimiting（与 Initializer 重复调用幂等无害）
        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName);

        // ② 编程式覆盖（PostConfigure：配置节之后应用 → 编程优先）
        if (configure != null)
            services.PostConfigure<RateLimitingOptions>(configure);

        // ③ 展开 ASP.NET Core AddRateLimiter：延迟经 IConfigureOptions 从最终 RateLimitingOptions
        //    （默认值 → 配置节 → 编程式）构建 RateLimiterOptions——避免调用时快照丢失配置节绑定。
        services.AddRateLimiter();
        services.ConfigureOptions<TkfwRateLimiterOptionsSetup>();

        return services;
    }

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
/// （默认值 → TKWF:RateLimiting 配置节 → AddTkfwRateLimiting(configure) 编程式，优先级递增）。
/// </summary>
internal sealed class TkfwRateLimiterOptionsSetup : IConfigureOptions<RateLimiterOptions>
{
    private readonly RateLimitingOptions _options;

    public TkfwRateLimiterOptionsSetup(IOptions<RateLimitingOptions> options)
        => _options = options.Value;

    public void Configure(RateLimiterOptions target)
        => RateLimitingServiceCollectionExtensions.ApplyTo(target, _options);
}