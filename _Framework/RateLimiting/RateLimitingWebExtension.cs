using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Web.Extensions;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// Web 层限流 Web 装配扩展（v4.10.45 Web 装配钩子——ADR87/D22/G18，内测期 4 先例收敛迁移）——
/// 自描述 Web 装配需求，消费方 <c>UseWebExtensions</c> 一次声明（替代旧静态方法
/// <c>AddTkfwRateLimiting</c> + <c>UseRateLimiter</c> 配对；CHANGELOG 破坏性变更）。
/// <para>锚点：默认 <see cref="MiddlewareAnchor.BeforeAuthentication"/>——自动路径感知模式
/// （全局分区器按 Request.Path 精确命中 EndpointPolicies）无碍；标注式 <c>RequireRateLimiting</c>
/// 需端点 metadata（UseRouting 后）——当前仍需消费方 BeforeRouting 显式 <c>app.UseRateLimiter()</c>
/// （AfterRouting 锚点为机制预留，G18 §5 说明；本扩展默认锚点不适用标注式）。</para>
/// <para>领域自治：Options 绑定 + <c>AddRateLimiter</c> 宿主注册属 Web 钩子；限流业务策略经
/// <c>TKWF:RateLimiting</c> 配置节 / <see cref="ConfigureOptions"/> 编程式设置。</para>
/// </summary>
public sealed class RateLimitingWebExtension : IWebExtension
{
    /// <summary>编程式配置（可选）——覆盖 <c>TKWF:RateLimiting</c> 配置节后应用（PostConfigure，编程优先；
    /// 保留旧 <c>AddTkfwRateLimiting(configure)</c> 能力）。</summary>
    public Action<RateLimitingOptions>? ConfigureOptions { get; set; }

    /// <summary>宿主注册（Build 前立即执行）：Options 绑定 + 展开 AddRateLimiter + 延迟 Setup。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // ① Options 绑定：配置节 TKWF:RateLimiting（与 Initializer 重复调用幂等无害）
        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName);

        // ② 编程式覆盖（PostConfigure：配置节之后应用 → 编程优先）
        if (ConfigureOptions is not null)
            services.PostConfigure<RateLimitingOptions>(ConfigureOptions);

        // ③ 展开 ASP.NET Core AddRateLimiter：延迟经 IConfigureOptions 从最终 RateLimitingOptions
        //    （默认值 → 配置节 → 编程式）构建 RateLimiterOptions——避免调用时快照丢失配置节绑定。
        services.AddRateLimiter();
        services.ConfigureOptions<TkfwRateLimiterOptionsSetup>();
    }

    /// <summary>中间件挂载（BeforeAuthentication 锚点桶排空时执行）——UseRateLimiter（Route 之前）。</summary>
    public void ConfigureMiddleware(IApplicationBuilder app, DomainWebOptions options)
        => app.UseRateLimiter();
}
