using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.HealthCheck
{
    /// <summary>
    /// 健康检查端点映射——<see cref="MapTkfwHealthChecks"/> 按 <see cref="HealthCheckEndpointOptions"/>
    /// （<c>TKWF:HealthCheck</c> 节）映射 <c>MapHealthChecks</c> 端点（默认 <c>/health</c>，D04 框架生命线已豁免认证）。
    /// <para>⚠️ 命名用 ASP.NET Core 内置 <see cref="Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions"/>
    /// （全限定）——与本扩展 <see cref="HealthCheckEndpointOptions"/> 明确区分（Oracle P1-1）。</para>
    /// </summary>
    public static class HealthCheckEndpointExtensions
    {
        /// <summary>
        /// 映射健康检查端点（默认 <c>/health</c>；<c>TKWF:HealthCheck:Path</c> 可配）。
        /// <para>Options 经可靠路径解析：优先 <see cref="IOptions{T}"/>（消费方经 AddTkfwHealthChecks /
        /// SG1 [Options] 绑定 / AddOptions 任一注册），未注册时兜底 <see cref="IConfiguration"/> 直读
        /// <c>TKWF:HealthCheck</c> 节，再兜底默认值。</para>
        /// <para>响应控制：<c>Detailed=false</c>（默认）输出仅 <c>{"status":"..."}</c>（不泄露组件细节）；
        /// <c>Detailed=true</c> 走自定义 ResponseWriter 输出组件级状态 JSON。<c>AllowAnonymous=true</c>（默认）时
        /// 端点追加 AllowAnonymous 元数据（与 D04 豁免一致）。</para>
        /// </summary>
        /// <param name="endpoints">端点路由构建器。</param>
        /// <returns>同一 <paramref name="endpoints"/>（链式）。</returns>
        public static IEndpointRouteBuilder MapTkfwHealthChecks(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var options = ResolveOptions(endpoints.ServiceProvider);
            if (!options.Enabled)
            {
                // TKWF:HealthCheck:Enabled=false —— 不映射端点
                return endpoints;
            }

            var healthCheckOptions = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                // 健康探测结果不应被 HTTP 缓存（诊断口径始终实时）
                AllowCachingResponses = false,
                ResponseWriter = options.Detailed
                    ? WriteDetailedResponseAsync
                    : WriteSummaryResponseAsync
            };

            var builder = endpoints.MapHealthChecks(options.Path, healthCheckOptions);

            if (options.AllowAnonymous)
            {
                builder.AllowAnonymous();
            }

            return endpoints;
        }

        /// <summary>可靠路径解析 Options——IOptions（消费方任一注册路径）→ IConfiguration 直读 → 默认值。</summary>
        private static HealthCheckEndpointOptions ResolveOptions(IServiceProvider services)
        {
            var options = services.GetService<IOptions<HealthCheckEndpointOptions>>()?.Value;
            if (options is not null)
            {
                return options;
            }

            var fallback = new HealthCheckEndpointOptions();
            services.GetService<IConfiguration>()?.GetSection("TKWF:HealthCheck").Bind(fallback);
            return fallback;
        }

        /// <summary>默认（Detailed=false）响应——仅状态，不泄露组件细节。</summary>
        private static Task WriteSummaryResponseAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            return context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
        }

        /// <summary>Detailed=true 响应——组件级状态 JSON（名称/状态/耗时/异常/描述/data）。</summary>
        private static Task WriteDetailedResponseAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = new
            {
                status = report.Status.ToString(),
                totalDuration = report.TotalDuration,
                entries = report.Entries.ToDictionary(
                    e => e.Key,
                    e => (object)new
                    {
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        duration = e.Value.Duration,
                        exception = e.Value.Exception?.GetType().Name,
                        data = e.Value.Data.Count > 0 ? e.Value.Data : null
                    })
            };
            return context.Response.WriteAsJsonAsync(payload);
        }
    }
}
