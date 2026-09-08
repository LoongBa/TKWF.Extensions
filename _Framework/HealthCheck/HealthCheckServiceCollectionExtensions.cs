using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.HealthCheck
{
    /// <summary>
    /// 健康检查服务注册接线入口——展开 <c>services.AddHealthChecks()</c>（net10 内置，零第三方依赖）+
    /// <see cref="HealthCheckEndpointOptions"/> Options 绑定（<c>TKWF:HealthCheck</c> 节）。
    /// <para>消费方探针经 ASP.NET Core 标准路径注册：<c>services.AddHealthChecks().AddCheck&lt;T&gt;("name")</c>——
    /// 扩展聚合全部检查项返回 OverallStatus（最差状态）。</para>
    /// </summary>
    public static class HealthCheckServiceCollectionExtensions
    {
        /// <summary>
        /// 注册健康检查服务（net10 内置 HealthChecks；零第三方依赖）。
        /// <para><paramref name="configure"/> 可选——应用 <see cref="HealthCheckEndpointOptions"/> 覆盖（代码配置优先于
        /// <c>TKWF:HealthCheck</c> 配置节绑定）。</para>
        /// </summary>
        /// <param name="services">服务集合。</param>
        /// <param name="configure">可选配置委托（后于配置节绑定应用）。</param>
        /// <returns>同一 <paramref name="services"/>（链式）。</returns>
        public static IServiceCollection AddTkfwHealthChecks(
            this IServiceCollection services,
            Action<HealthCheckEndpointOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            // net10 内置 HealthChecks：注册 HealthCheckService + IHealthCheck 聚合（消费方经 AddCheck 注册探针）
            services.AddHealthChecks();

            // Options 绑定 TKWF:HealthCheck 节（配置节绑定先，configure 委托后——代码覆盖配置）
            var optionsBuilder = services
                .AddOptions<HealthCheckEndpointOptions>()
                .BindConfiguration("TKWF:HealthCheck");
            if (configure is not null)
            {
                optionsBuilder.Configure(configure);
            }

            return services;
        }
    }
}
