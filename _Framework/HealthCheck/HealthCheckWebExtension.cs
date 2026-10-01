using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Web.Extensions;

namespace TKWF.Ext.HealthCheck;

/// <summary>
/// 系统健康探测 Web 装配扩展（v4.10.45 Web 装配钩子——ADR87/D22/G18，内测期 4 先例收敛迁移）——
/// 自描述 Web 装配需求，消费方 <c>UseWebExtensions</c> 一次声明（替代旧静态方法
/// <c>AddTkfwHealthChecks</c> + <c>MapTkfwHealthChecks</c> 配对；CHANGELOG 破坏性变更）。
/// <para>无中间件（不实现 <c>ConfigureMiddleware</c>——接口默认空实现）；仅宿主注册 + 端点映射。</para>
/// <para>fluent 收集（G18 §3，configure 先时序兼容）：<see cref="AddDatabaseHealthCheck{TEntity}"/> /
/// <see cref="AddCheck{THealthCheck}"/> 收集委托到 <c>List&lt;Action&lt;IHealthChecksBuilder&gt;&gt;</c>，
/// <see cref="ConfigureServices"/> 内 <c>AddHealthChecks()</c> 后逐项执行——消费方 configure 委托先于
/// ConfigureServices 调用，收集项在此已就绪（零延迟闭包）。</para>
/// <para>端点映射：<see cref="ConfigureEndpoints"/> 内 <c>MapHealthChecks</c>——<c>TKWF:HealthCheck</c>
/// 节（<see cref="HealthCheckEndpointOptions"/>）控制 Path/Enabled/Detailed/AllowAnonymous（与 D04 生命线豁免一致）。</para>
/// </summary>
public sealed class HealthCheckWebExtension : IWebExtension
{
    private readonly List<Action<IHealthChecksBuilder>> _checks = [];

    /// <summary>端点 Options 编程式配置（可选）——后于 <c>TKWF:HealthCheck</c> 配置节绑定应用（代码覆盖配置；
    /// 保留旧 <c>AddTkfwHealthChecks(configure)</c> 能力）。</summary>
    public Action<HealthCheckEndpointOptions>? ConfigureOptions { get; set; }

    /// <summary>收集 DB 连通性探针（V0.2.0）——委托 <see cref="HealthCheckBuilderExtensions.AddDatabaseHealthCheck{TEntity}"/>
    /// 在 ConfigureServices 内执行（配置节绑定后）。</summary>
    public HealthCheckWebExtension AddDatabaseHealthCheck<TEntity>(
        string name,
        HealthStatus? failureStatus = null,
        TimeSpan? timeout = null)
        where TEntity : class, IDomainEntity, new()
    {
        _checks.Add(b => b.AddDatabaseHealthCheck<TEntity>(name, failureStatus, timeout));
        return this;
    }

    /// <summary>收集消费方自定义探针（ASP.NET Core 标准 <c>AddCheck&lt;T&gt;</c>——保留旧
    /// <c>AddTkfwHealthChecks().AddCheck&lt;T&gt;("name")</c> 链式能力）。</summary>
    public HealthCheckWebExtension AddCheck<THealthCheck>(
        string name,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
        where THealthCheck : class, IHealthCheck
    {
        _checks.Add(b => b.AddCheck<THealthCheck>(name, failureStatus, tags, timeout));
        return this;
    }

    /// <summary>宿主注册（Build 前立即执行）：AddHealthChecks + Options 绑定 + 逐项执行收集的探针委托。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // net10 内置 HealthChecks：注册 HealthCheckService + IHealthCheck 聚合
        var builder = services.AddHealthChecks();

        // Options 绑定 TKWF:HealthCheck 节（配置节绑定先，ConfigureOptions 委托后——代码覆盖配置）
        var optionsBuilder = services
            .AddOptions<HealthCheckEndpointOptions>()
            .BindConfiguration("TKWF:HealthCheck");
        if (ConfigureOptions is not null)
        {
            optionsBuilder.Configure(ConfigureOptions);
        }

        // 逐项执行收集的探针注册（configure 先时序——收集项已就绪）
        foreach (var check in _checks)
        {
            check(builder);
        }
    }

    /// <summary>端点映射（RoutingBuilder.AfterRouting 排空）——按 Options 映射 MapHealthChecks（默认 /health，D04 已豁免认证）。</summary>
    public void ConfigureEndpoints(IEndpointRouteBuilder endpoints, DomainWebOptions options)
    {
        var endpointOptions = ResolveOptions(endpoints.ServiceProvider);
        if (!endpointOptions.Enabled)
        {
            // TKWF:HealthCheck:Enabled=false —— 不映射端点
            return;
        }

        var healthCheckOptions = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            // 健康探测结果不应被 HTTP 缓存（诊断口径始终实时）
            AllowCachingResponses = false,
            ResponseWriter = endpointOptions.Detailed
                ? WriteDetailedResponseAsync
                : WriteSummaryResponseAsync
        };

        var builder = endpoints.MapHealthChecks(endpointOptions.Path, healthCheckOptions);

        if (endpointOptions.AllowAnonymous)
        {
            builder.AllowAnonymous();
        }
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
