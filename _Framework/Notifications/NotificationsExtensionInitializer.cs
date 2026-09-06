using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Notifications;

/// <summary>
/// Notifications 扩展初始化器——经 <c>[TKWFExtension]</c> 被 SG1 发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——注册 DefinitionManager(Singleton) + Publisher/Store/SubscriptionManager/InboxNotifier(Scoped)
///       + <see cref="NotificationsOptions"/> Options 绑定（TKWF:Notifications）</item>
/// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
/// <item>InitializeAsync——不调用（V0.1.0 无种子数据）</item>
/// </list>
/// <para>IPermissionChecker 不注册——由消费方 Permissions 扩展提供；发布方权限门控经
/// <see cref="NotificationPublisher"/> 的 IServiceProvider 可空解析（C1）。</para>
/// </summary>
[TKWFExtension("Notifications")]
public class NotificationsExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "Notifications";

    /// <summary>扩展描述。</summary>
    public override string Description => "通知中心（站内通知收件箱 + 订阅 + 事件驱动通知 + 多通道抽象）";

    /// <summary>
    /// 注册 Notifications 服务。
    /// <para>TryAddScoped/TryAddSingleton：消费方可自定义实现，扩展默认实现不覆盖消费方。</para>
    /// <para>m2：DefinitionManager = Singleton（定义启动时收集后不可变）；其余 = Scoped。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 绑定：TKWF:Notifications 配置节 + 默认值
        services.AddOptions<NotificationsOptions>()
            .BindConfiguration("TKWF:Notifications");

        // 通知定义 Provider 发现（M3 修订）：反射扫描已加载程序集中标 [NotificationDefinitionProvider] 的类，
        // 注册为 INotificationDefinitionProvider（启动一次性成本可接受）。
        // 对齐 Navigation [MenuContributor] 先例——特性驱动 + DI 收集。
        RegisterNotificationDefinitionProviders(services);

        // 定义管理器（Singleton：定义启动时收集后不可变，m2）
        services.TryAddSingleton<NotificationDefinitionManager>();
        services.TryAddSingleton<INotificationDefinitionManager>(sp => sp.GetRequiredService<NotificationDefinitionManager>());

        // 数据访问红线整改（2026-09-07）：委托 SG1 DataService，禁裸 IFreeSql

        // 收件箱存储（Scoped）
        services.TryAddScoped<NotificationStore>();
        services.TryAddScoped<INotificationStore>(sp => sp.GetRequiredService<NotificationStore>());

        // 订阅管理（Scoped）
        services.TryAddScoped<NotificationSubscriptionStore>();
        services.TryAddScoped<INotificationSubscriptionManager>(sp => sp.GetRequiredService<NotificationSubscriptionStore>());

        // 通道（Scoped：InboxNotifier owns UserNotification 写入，C5）
        services.TryAddScoped<InboxNotifier>();
        services.TryAddScoped<INotificationNotifier>(sp => sp.GetRequiredService<InboxNotifier>());

        // 发布器（Scoped）
        services.TryAddScoped<NotificationPublisher>();
        services.TryAddScoped<INotificationPublisher>(sp => sp.GetRequiredService<NotificationPublisher>());
    }

    /// <summary>
    /// 扫描已加载程序集（不含动态/系统程序集），注册 [NotificationDefinitionProvider] 标记类。
    /// <para>TryAddEnumerable：消费方手动注册的 Provider 优先，特性扫描不覆盖；重复注册自动去重。</para>
    /// </summary>
    private static void RegisterNotificationDefinitionProviders(IServiceCollection services)
    {
        var providerType = typeof(INotificationDefinitionProvider);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly.FullName?.StartsWith("System.", StringComparison.Ordinal) == true
                || assembly.FullName?.StartsWith("Microsoft.", StringComparison.Ordinal) == true)
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                continue; // 跳过不可完整加载的程序集
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface || !providerType.IsAssignableFrom(type))
                    continue;
                if (type.GetCustomAttribute<NotificationDefinitionProviderAttribute>() == null)
                    continue;

                services.TryAddEnumerable(ServiceDescriptor.Singleton(providerType, type));
            }
        }
    }
}