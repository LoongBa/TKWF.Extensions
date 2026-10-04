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
/// <item><see cref="ConfigureServices"/>——注册四态服务：
///       <b>AddConstructibleService 门面</b>（<see cref="INotificationStore"/>/<see cref="INotificationSubscriptionManager"/>/
///       <see cref="INotificationPreferenceManager"/>/<see cref="INotificationPublisher"/>——接口可构造守卫工厂 + 实现类 throw-factory）+
///       <b>TryAddSingleton 定义管理</b>（<see cref="INotificationDefinitionManager"/>，Singleton）+
///       <b>TryAddScoped 接线型 + TryAddEnumerable 通道</b>（<see cref="InboxNotifier"/>/<see cref="EmailNotifier"/>——多实例收集）+
///       <see cref="NotificationsOptions"/> Options 绑定（TKWF:Notifications）</item>
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
    /// <para>V4.10.53（领域自治根治，ADR90 正确路线）注册形态四态：</para>
    /// <list type="bullet">
    /// <item><b>AddConstructibleService 门面</b>——4 标准门面（<see cref="INotificationStore"/>/<see cref="INotificationSubscriptionManager"/>/
    ///      <see cref="INotificationPreferenceManager"/>/<see cref="INotificationPublisher"/>）：接口可构造守卫工厂
    ///      （CurrentAopUser 守卫——域作用域外解析即抛）+ 实现类 throw-factory；消费方统一经
    ///      <c>User.Use&lt;接口&gt;()</c> 解析（AOP 路径）。旧 TryAddScoped 构造注入 <see cref="IDomainUser"/>
    ///      而 IDomainUser 永不注册 DI（D01）→ 生产解析必失败（v0.3.3 同根缺陷）。</item>
    /// <item><b>TryAddSingleton 定义管理</b>——<see cref="INotificationDefinitionManager"/>（m2：定义启动时收集后不可变）。</item>
    /// <item><b>TryAddScoped 接线型 + TryAddEnumerable 通道</b>——<see cref="InboxNotifier"/>/<see cref="EmailNotifier"/>
    ///      多实例收集（v0.2.0：InboxNotifier owns UserNotification 写入 C5；EmailNotifier 外部通道 best-effort M1，
    ///      接线型 ctor(IServiceProvider,ILogger) 无 user 依赖——对齐 UserCenter 先例）。
    ///      V4.10.55（ADR92，T3 闭环）：<see cref="InboxNotifier"/> 改 <b>TryAddEnumerableConstructible</b>
    ///      （集合版守卫工厂——帧内 CurrentAopUser 供给 ctor 的 IDomainUser，帧外枚举抛守卫）+ 继承
    ///      <see cref="DomainServiceBase"/>；<see cref="EmailNotifier"/> 保持接线型普通 DI（无 user 依赖，可解析）。</item>
    /// </list>
    /// <para>TryAdd* 幂等：消费方可自定义实现，扩展默认实现不覆盖消费方。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 绑定：TKWF:Notifications 配置节 + 默认值
        services.AddOptions<NotificationsOptions>()
            .BindConfiguration("TKWF:Notifications");

        // 通知定义 Provider 发现（M3 修订）：反射扫描已加载程序集中标 [NotificationDefinitionProvider] 的类，
        // 注册为 INotificationDefinitionProvider（启动一次性成本可接受）。
        // 对齐 Navigation IMenuContributor 接口判定先例——特性驱动 + DI 收集。
        RegisterNotificationDefinitionProviders(services);

        // ── TryAddSingleton 定义管理（m2：Singleton——定义启动时收集后不可变）──
        services.TryAddSingleton<NotificationDefinitionManager>();
        services.TryAddSingleton<INotificationDefinitionManager>(sp => sp.GetRequiredService<NotificationDefinitionManager>());

        // 数据访问红线整改（2026-09-07）：委托 SG1 DataService，禁裸 IFreeSql
        // v4.10.8 (ADR61) 起：SG1 基类类型判定 + 消费方聚合自动注册扩展 DataService（可构造工厂）——
        // 不再手动 TryAddScoped（含 VEntity 只读 DataService，经 DomainReadOnlyDataServiceBase 基类判定覆盖）。

        // ── AddConstructibleService 门面（V4.10.53 领域自治根治）──
        // 收件箱存储门面：接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory
        services.AddConstructibleService<INotificationStore, NotificationStore>();

        // 订阅管理门面
        services.AddConstructibleService<INotificationSubscriptionManager, NotificationSubscriptionStore>();

        // V0.3.0：通知偏好门面——用户通道偏好覆盖定义级 UseChannels
        services.AddConstructibleService<INotificationPreferenceManager, NotificationPreferenceStore>();

        // ── TryAddScoped 接线型 + TryAddEnumerable 通道（v0.2.0 多实例收集：InboxNotifier owns UserNotification
        //    写入 C5；EmailNotifier 外部通道 best-effort M1，延迟解析 IEmailSender/IUserEmailProvider）──
        services.TryAddScoped<EmailNotifier>();
        services.TryAddEnumerableConstructible<INotificationNotifier, InboxNotifier>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INotificationNotifier, EmailNotifier>());

        // ── AddConstructibleService 门面 ──
        // 发布器门面
        services.AddConstructibleService<INotificationPublisher, NotificationPublisher>();
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