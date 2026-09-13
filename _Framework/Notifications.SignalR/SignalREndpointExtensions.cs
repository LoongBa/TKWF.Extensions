using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.Notifications.SignalR;

/// <summary>
/// SignalR 通知端点映射——<see cref="MapTkfwNotificationsHub"/> 按 <see cref="NotificationsSignalROptions"/>
/// （<c>TKWF:Notifications:SignalR</c> 节）映射 <c>MapHub&lt;NotificationsHub&gt;</c> 端点（默认 <c>/hubs/notifications</c>）。
/// <para>P2-6 可靠路径：Options 经 <see cref="IOptions{T}"/> 解析（Initializer 已 AddOptions 绑定），
/// 未注册时兜底默认值——对齐 HealthCheck <c>MapTkfwHealthChecks</c> ResolveOptions 模式。</para>
/// <para>认证：默认 <b>RequireAuthorization</b>（通知负载敏感须登录——与 /health 生命线豁免相反，D7）；
/// <c>AllowAnonymous=true</c> 时追加 AllowAnonymous 元数据。SignalR token 认证管线（WebSocket 不支持自定义 header，
/// JWT 经 query string <c>?access_token=...</c> 传递）由消费方 <c>AddJwtBearer</c> events 处理——扩展只追加授权元数据，
/// 不实现认证（D6，接线型边界）。</para>
/// </summary>
public static class SignalREndpointExtensions
{
    /// <summary>
    /// 映射通知 Hub 端点（默认 <c>/hubs/notifications</c>；<c>TKWF:Notifications:SignalR:Path</c> 可配）。
    /// <para>消费方 Program.cs 调用：<c>app.MapTkfwNotificationsHub()</c>（须已 <c>services.AddSignalR()</c>）。</para>
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>同一 <paramref name="endpoints"/>（链式）。</returns>
    public static IEndpointRouteBuilder MapTkfwNotificationsHub(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<IOptions<NotificationsSignalROptions>>()?.Value
            ?? new NotificationsSignalROptions();

        var builder = endpoints.MapHub<NotificationsHub>(options.Path);

        if (options.AllowAnonymous)
        {
            builder.AllowAnonymous();
        }
        else
        {
            builder.RequireAuthorization();
        }

        return endpoints;
    }
}