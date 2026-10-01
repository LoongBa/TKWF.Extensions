using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Web.Extensions;

namespace TKWF.Ext.Notifications.SignalR;

/// <summary>
/// 通知中心 SignalR 实时推送通道 Web 装配扩展（v4.10.45 Web 装配钩子——ADR87/D22/G18，内测期 4 先例收敛迁移）——
/// 自描述 Web 装配需求，消费方 <c>UseWebExtensions</c> 一次声明（替代旧 <c>MapTkfwNotificationsHub</c>
/// 静态方法 + 消费方自调 <c>AddSignalR</c>；CHANGELOG 破坏性变更）。
/// <para>D6 边界内聚（2026-10-01 语义推移：消费方 → 扩展）：<see cref="ConfigureServices"/> 内 <c>AddSignalR()</c>
/// ——消费方零宿主注册负担（<c>AddSignalR</c> 幂等，既有消费方自调无碍）。</para>
/// <para>无中间件（不实现 <see cref="ConfigureMiddleware"/>——接口默认空实现）。</para>
/// <para>端点映射：<see cref="ConfigureEndpoints"/> 内 <c>MapHub&lt;NotificationsHub&gt;</c>（默认 <c>/hubs/notifications</c>，
/// <c>TKWF:Notifications:SignalR</c> 节）——默认 <b>RequireAuthorization</b>（通知负载敏感须登录，D7）；
/// <c>AllowAnonymous=true</c> 时追加 AllowAnonymous 元数据。SignalR token 认证管线（WebSocket 不支持自定义 header，
/// JWT 经 query string <c>?access_token=...</c>）由消费方 <c>AddJwtBearer</c> events 处理——扩展只追加授权元数据，
/// 不实现认证（接线型边界）。</para>
/// </summary>
public sealed class NotificationsHubWebExtension : IWebExtension
{
    /// <summary>宿主注册（Build 前立即执行）：AddSignalR（D6 边界内聚——幂等）+ Options 绑定（TKWF:Notifications:SignalR 节）。</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // D6 边界内聚（2026-10-01）：SignalR 基础设施宿主注册从消费方职责收敛进扩展（AddSignalR 幂等无碍）
        services.AddSignalR();

        // Options 绑定：TKWF:Notifications:SignalR 配置节 + 默认值（显式路径，非 SG1 [Options]）
        services.AddOptions<NotificationsSignalROptions>()
            .BindConfiguration("TKWF:Notifications:SignalR");
    }

    /// <summary>端点映射（RoutingBuilder.AfterRouting 排空）——MapHub&lt;NotificationsHub&gt; + 授权元数据。</summary>
    public void ConfigureEndpoints(IEndpointRouteBuilder endpoints, DomainWebOptions options)
    {
        var opts = endpoints.ServiceProvider.GetService<IOptions<NotificationsSignalROptions>>()?.Value
            ?? new NotificationsSignalROptions();

        var builder = endpoints.MapHub<NotificationsHub>(opts.Path);

        if (opts.AllowAnonymous)
        {
            builder.AllowAnonymous();
        }
        else
        {
            builder.RequireAuthorization();
        }
    }
}
