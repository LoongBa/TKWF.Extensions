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
/// <item><see cref="ConfigureServices"/>——注册 DefinitionManager(Singleton) + Publisher/Store/SubscriptionManager/通道(Scoped)
///       + <see cref="NotificationsOptions"/> Options 绑定（TKWF:Notifications）</item>
/// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
/// <item>InitializeAsync——不调用（V0.1.0 无种子数据）</item>
/// </list>
/// <para>IPermissionChecker 不注册——由消费方 Permissions 扩展提供；发布方权限门控经
/// <see cref="NotificationPublisher"/> 的 IServiceProvider 可空解析（C1）。</para>
/// <para>IUserEmailProvider 不注册——由消费方提供（Email 通道收件地址来源，V0.2.0）。</para>
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
        // 2026-09-12 修正（Oracle 裁决）：扩展 DataService 必须显式注册——SG 自动注册不覆盖扩展 DataService：
        //   ① SG1a IsCandidateClass 要求显式 public/internal 修饰符；生成的 DataService 为隐式 internal，不被采集 → 无元数据；
        //   ② 消费方 RegisterGeneratedServices 仅消费自身 ProjectMetaContext，不聚合扩展上下文；
        //   ③ 即便被发现，AddService 注册的是 throw-factory（供 User.Use<T>()），会击穿 Store 构造注入。
        //（VEntity 另因 xCodeGen 跳过其 DataService 模板，同样需手动注册。）
        services.TryAddScoped<NotificationEntityDataService>();
        services.TryAddScoped<UserNotificationEntityDataService>();
        services.TryAddScoped<NotificationSubscriptionEntityDataService>();
        services.TryAddScoped<NotificationPreferenceEntityDataService>();   // V0.3.0：偏好表
        services.TryAddScoped<UserNotificationViewDataService>();            // V0.2.0 VEntity：手写只读 DataService

        // 收件箱存储（Scoped）
        services.TryAddScoped<NotificationStore>();
        services.TryAddScoped<INotificationStore>(sp => sp.GetRequiredService<NotificationStore>());

        // 订阅管理（Scoped）
        services.TryAddScoped<NotificationSubscriptionStore>();
        services.TryAddScoped<INotificationSubscriptionManager>(sp => sp.GetRequiredService<NotificationSubscriptionStore>());

        // V0.3.0：通知偏好（Scoped）——用户通道偏好覆盖定义级 UseChannels
        services.TryAddScoped<NotificationPreferenceStore>();
        services.TryAddScoped<INotificationPreferenceManager>(sp => sp.GetRequiredService<NotificationPreferenceStore>());

        // 通道（Scoped 多实例收集 v0.2.0：TryAddEnumerable——InboxNotifier owns UserNotification 写入 C5；
        // EmailNotifier 外部通道 best-effort M1，延迟解析 IEmailSender/IUserEmailProvider）
        services.TryAddScoped<InboxNotifier>();
        services.TryAddScoped<EmailNotifier>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INotificationNotifier, InboxNotifier>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INotificationNotifier, EmailNotifier>());

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