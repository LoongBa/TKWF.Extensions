using Microsoft.AspNetCore.Builder;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// 端点限流辅助——为端点应用命名限流策略（可选；消费方也可标准 <c>RequireRateLimiting</c>）。
/// <para><b>端点策略的两条生效路径</b>（Oracle P2-2 精确路径匹配）：</para>
/// <list type="number">
/// <item><b>自动</b>：<c>AddTkfwRateLimiting</c> 注册的全局分区器按 <c>Request.Path</c> 精确命中
///   <see cref="RateLimitingOptions.EndpointPolicies"/> 自动应用端点策略（独立配额）——无需标注端点。</item>
/// <item><b>标注式</b>：端点定义时经 <see cref="MapTkfwRateLimiter(IEndpointConventionBuilder, string)"/>
///   （policyName = EndpointPolicies 路径键）或标准 <c>RequireRateLimiting</c> 显式挂策略。</item>
/// </list>
/// <para>端点被显式挂命名策略后，RateLimiter 中间件命中该端点时只执行端点策略、不执行全局策略——
/// 两条路径互补不叠加。</para>
/// </summary>
public static class RateLimitingEndpointExtensions
{
    /// <summary>
    /// 为正在定义的端点挂限流策略。
    /// </summary>
    /// <param name="endpoint">端点约定构建器（如 <c>app.MapGet("/api/x", ...)</c> 返回值）。</param>
    /// <param name="policyName">命名限流策略名（<c>TKWF:RateLimiting:EndpointPolicies</c> 的路径键，
    /// 或消费方经 <c>AddPolicy</c> 自定义注册的其它策略名）。</param>
    /// <returns>同一端点约定构建器（可链式调用）。</returns>
    /// <remarks>
    /// 注意：端点实例在路由构建后 metadata 不可变——本辅助仅用于<b>端点定义时</b>
    /// （MapGet/MapPost 返回值），不可用于已构建的 EndpointDataSource。
    /// </remarks>
    public static IEndpointConventionBuilder MapTkfwRateLimiter(
        this IEndpointConventionBuilder endpoint,
        string policyName)
        => endpoint.RequireRateLimiting(policyName);
}