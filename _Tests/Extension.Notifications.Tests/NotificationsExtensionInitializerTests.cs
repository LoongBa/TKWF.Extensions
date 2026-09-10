using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// <see cref="NotificationsExtensionInitializer{TUserInfo}"/> DI 注册测试——Publisher/Store/SubscriptionManager/
/// DefinitionManager（Singleton）/InboxNotifier/Options + TKWF:Notifications 配置节绑定。
/// <para>对齐 DataPortExtensionInitializerTests 模式：直接实例化初始化器 + ConfigureServices + Descriptor/容器验证。</para>
/// </summary>
public class NotificationsExtensionInitializerTests
{
    private const string TkfwNotificationsSection = "TKWF:Notifications";

    [Fact]
    public void ConfigureServices_RegistersPublisher()
    {
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(INotificationPublisher));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_RegistersStore()
    {
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(INotificationStore));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_RegistersSubscriptionManager()
    {
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(INotificationSubscriptionManager));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_RegistersDefinitionManager()
    {
        var services = new ServiceCollection();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(INotificationDefinitionManager));

        // m2：定义管理器为 Singleton（通知定义进程级静态，收集后不可变）
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void ConfigureServices_RegistersNotifiers_MultiInstanceCollection()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        // 用 Build 容器（含 DataService 注册——模拟 SG 自动注册，Notifier 可解析）
        using var sp = NotificationTestHost.Build(fsql);

        // v0.2.0：多实例收集（TryAddEnumerable）——Inbox + Email 两个内置通道
        var notifiers = sp.GetServices<INotificationNotifier>().ToList();

        Assert.Equal(2, notifiers.Count);
        Assert.Contains(notifiers, n => n.Name == "Inbox");
        Assert.Contains(notifiers, n => n.Name == "Email");

        // Inbox 通道： Email 通道外部 best-effort
        Assert.Contains(notifiers, n => n is InboxNotifier);
        Assert.Contains(notifiers, n => n is EmailNotifier);
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
