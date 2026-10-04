using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKWF.Ext.Notifications;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// <see cref="NotificationsExtensionInitializer{TUserInfo}"/> DI 注册测试——Publisher/Store/SubscriptionManager/
/// DefinitionManager（Singleton）/InboxNotifier/Options + TKWF:Notifications 配置节绑定。
/// <para>V4.10.53（领域自治根治）：4 标准门面注册形态由 TryAddScoped 改 <c>AddConstructibleService</c>——
/// 接口注册为可构造守卫工厂（非实现映射，域作用域外解析即抛）+ 实现类注册为 throw-factory（禁直接 DI 解析）；
/// 消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析（AOP 路径先设 CurrentAopUser 再 GetRequiredService）。
/// 对齐 SettingsExtensionInitializerTests 断言模式。</para>
/// </summary>
public class NotificationsExtensionInitializerTests
{
    private const string TkfwNotificationsSection = "TKWF:Notifications";

    [Fact]
    public void ConfigureServices_RegistersPublisher()
    {
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口注册为可构造守卫工厂（非实现映射），Scoped 生命周期
        var descriptor = services.First(d => d.ServiceType == typeof(INotificationPublisher));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Null(descriptor.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_RegistersStore()
    {
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口守卫工厂（同 Publisher）
        var descriptor = services.First(d => d.ServiceType == typeof(INotificationStore));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Null(descriptor.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_RegistersSubscriptionManager()
    {
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口守卫工厂（同 Publisher）
        var descriptor = services.First(d => d.ServiceType == typeof(INotificationSubscriptionManager));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Null(descriptor.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_RegistersDefinitionManager()
    {
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(INotificationDefinitionManager));

        // m2：定义管理器为 Singleton（通知定义进程级静态，收集后不可变）——接线型保留（TryAddSingleton 普通 DI）
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_Registers_NotificationStore_ThrowFactory()
    {
        // V4.10.53：实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<INotificationStore>() 创建
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(NotificationStore));
        Assert.NotNull(descriptor.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<NotificationStore>());
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // V4.10.53：接口守卫工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<INotificationPublisher>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("INotificationPublisher", ex.Message);
    }

    [Fact]
    public void ConfigureServices_RegistersNotifiers_MultiInstanceCollection()
    {
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // v0.2.0：多实例收集——Inbox + Email 两个内置通道
        // V4.10.55 (ADR92)：InboxNotifier 改 TryAddEnumerableConstructible（集合版守卫工厂——工厂委托形态，
        // 帧内 CurrentAopUser 供给）；EmailNotifier 保持接线型普通 DI（实现映射可解析）
        var notifierDescriptors = services.Where(d => d.ServiceType == typeof(INotificationNotifier)).ToList();
        Assert.Equal(2, notifierDescriptors.Count);
        Assert.Contains(notifierDescriptors, d => d.ImplementationFactory != null && d.ImplementationType == null); // Inbox = 守卫工厂
        Assert.Contains(notifierDescriptors, d => d.ImplementationType == typeof(EmailNotifier));                    // Email = 接线型
        Assert.All(notifierDescriptors, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }

    /// <summary>帧内验证：发布器门面经 User.Use（AOP 帧）创建时，ctor 注入的 IEnumerable&lt;INotificationNotifier&gt;
    /// 在集合版守卫工厂帧内枚举成功（InboxNotifier 经 CurrentAopUser 构造 + EmailNotifier 普通可解析）——帧外
    /// <c>GetServices&lt;INotificationNotifier&gt;()</c> 会触发守卫（禁止形态）。</summary>
    [Fact]
    public void Publisher_Resolves_InFrame_WithInboxAndEmailChannels()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var host = NotificationTestHost.Build(fsql);

        Assert.NotNull(host.Publisher);
    }

    [Fact]
    public void ConfigureServices_RegistersOptions()
    {
        var services = new ServiceCollection();
        // BindConfiguration("TKWF:Notifications") 在 Options 解析时经 IConfiguration 绑定——测试注册空配置即可验证默认值
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfigureOptions<NotificationsOptions>));

        Assert.NotNull(descriptor);

        // 默认值（未配置 → RecipientBatchSize=256）
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<NotificationsOptions>>().Value;
        Assert.Equal(256, options.RecipientBatchSize);
    }

    [Fact]
    public void ConfigureOptions_BindsConfigurationSection()
    {
        // 验证 BindConfiguration("TKWF:Notifications") 生效——配置覆盖默认值
        var services = new ServiceCollection();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TkfwNotificationsSection}:{nameof(NotificationsOptions.RecipientBatchSize)}"] = "512"
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<NotificationsOptions>>().Value;

        Assert.Equal(512, options.RecipientBatchSize);
    }
}
