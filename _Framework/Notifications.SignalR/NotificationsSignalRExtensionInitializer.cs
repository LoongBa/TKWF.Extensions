using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.SignalR;

/// <summary>
/// Notifications SignalR 通道扩展初始化器——经 <c>[TKWFExtension]</c> 被 SG1 发现。
/// <para><b>启用白名单（P2-5 澄清）</b>：本初始器只标 <c>[TKWFExtension]</c>（SG1 发现生成能力清单）；
/// <b>消费方 DomainHostInitializer 标</b> <c>[TKWFEnabledExtension(typeof(NotificationsSignalRExtensionInitializer&lt;&gt;))]</c>
/// 白名单声明后三钩子才执行（发现 ≠ 启用，AGENTS.md §8）——须与主包
/// <c>[TKWFEnabledExtension(typeof(NotificationsExtensionInitializer&lt;&gt;))]</c> 同时声明。</para>
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——注册 <see cref="SignalRNotifier"/> 到 <see cref="INotificationNotifier"/> 集合
///       （TryAddEnumerable，与主包 Inbox/Email 共存）+ Options 绑定（TKWF:Notifications:SignalR 节，P2-6 显式 AddOptions 路径）</item>
/// <item>ConfigureFilters——不调用（无过滤器）</item>
/// <item>InitializeAsync——不调用（无种子/无持久化）</item>
/// </list>
/// <para><b>不调 <c>AddSignalR()</c></b>（接线型边界 D6）：SignalR 基础设施（IHubContext 注册）归消费方
/// <c>services.AddSignalR()</c>；<see cref="SignalRNotifier"/> 延迟可空解析 <see cref="IHubContext{NotificationsHub}"/>
/// ——未 AddSignalR 时构造不失败、投递 LogWarning 跳过。</para>
/// </summary>
[TKWFExtension("Notifications.SignalR")]
public class NotificationsSignalRExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "Notifications.SignalR";

    /// <summary>扩展描述。</summary>
    public override string Description => "通知中心 SignalR 实时推送通道（服务端——Hub 类型锚 + SignalRNotifier best-effort 推送 + 端点映射；消费方 AddSignalR + MapTkfwNotificationsHub 接线）";

    /// <summary>
    /// 注册 SignalR 通道服务。
    /// <para><c>NotificationsSignalROptions</c> 绑定 <c>TKWF:Notifications:SignalR</c> 节——显式
    /// <c>AddOptions().BindConfiguration</c>（P2-6：接线型扩展无 SG1 实体，不依赖 SG1 [Options] 自动绑定；
    /// 对齐主包 NotificationsOptions 现模式）。</para>
    /// <para><see cref="SignalRNotifier"/> 注册为 Scoped（对齐主包 Inbox/Email 通道）+ TryAddEnumerable
    /// 加入 <see cref="INotificationNotifier"/> 集合——发布时按 <c>Name=="SignalR"</c> 匹配投递。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 绑定：TKWF:Notifications:SignalR 配置节 + 默认值（显式路径，非 SG1 [Options]）
        services.AddOptions<NotificationsSignalROptions>()
            .BindConfiguration("TKWF:Notifications:SignalR");

        // SignalR 通道（Scoped，多实例集合——与主包 Inbox/Email 共存；延迟解析 IHubContext，未 AddSignalR 不失败）
        services.TryAddScoped<SignalRNotifier>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INotificationNotifier, SignalRNotifier>());
    }
}