using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// <see cref="INotificationDefinitionManager"/> 通知定义管理器测试——Provider 收集、查询、重复定义校验、Fluent 权限 API。
/// <para>定义经 DI 注册的 <see cref="INotificationDefinitionProvider"/> 收集（见 <see cref="NotificationTestHost"/>）。</para>
/// </summary>
public class NotificationDefinitionManagerTests
{
    [Fact]
    public void GetAll_CollectsProviderDefinitions()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationDefinitionManager>();

        var all = manager.GetAll();

        // 3 = 手动注册的 TestNotificationDefinitions(2) + M3 特性扫描发现的 AttributedDiscoveredDefinitions(1)
        Assert.Equal(3, all.Count);
        Assert.Contains(all, d => d.Name == TestNotificationDefinitions.OrderShipped);
        Assert.Contains(all, d => d.Name == TestNotificationDefinitions.SystemAlert);
        Assert.Contains(all, d => d.Name == AttributedDiscoveredDefinitions.AttributedAlert);
    }

    [Fact]
    public void Get_Existing_ReturnsDefinition()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationDefinitionManager>();

        var def = manager.Get(TestNotificationDefinitions.OrderShipped);

        Assert.NotNull(def);
        Assert.Equal("订单已发货", def.DisplayName);
        Assert.Equal(NotificationSeverity.Info, def.Severity);
    }

    [Fact]
    public void Get_Missing_Throws()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationDefinitionManager>();

        // 实现约定：未注册定义抛 KeyNotFoundException
        Assert.Throws<KeyNotFoundException>(() => manager.Get("NoSuchNotification"));
    }

    [Fact]
    public void Exists_TrueFalse()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationDefinitionManager>();

        Assert.True(manager.Exists(TestNotificationDefinitions.OrderShipped));
        Assert.False(manager.Exists("NoSuchNotification"));
    }

    [Fact]
    public void DuplicateDefinition_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationDefinitionProvider, TestNotificationDefinitions>();
        services.AddSingleton<INotificationDefinitionProvider, DuplicateOrderShippedDefinitions>();
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 重复定义（同名 "OrderShipped"）→ 管理器首次访问（懒加载收集）时抛异常
        var exception = Record.Exception(() =>
        {
            using var sp = services.BuildServiceProvider();
            var manager = sp.GetRequiredService<INotificationDefinitionManager>();
            _ = manager.GetAll(); // 触发 Provider 收集 → 第二个同名定义 Add 抛异常
        });

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    [Fact]
    public void RequirePermission_SetsPermissionName()
    {
        var def = new NotificationDefinition("RestrictedAlert", "受限公告")
            .RequirePermission("Notifications.Restricted");

        Assert.Equal("Notifications.Restricted", def.PermissionName);
    }

    // ─── M3 修订：特性驱动 Provider 发现 ───

    [Fact]
    public void ConfigureServices_AttributeScannedProvider_IsCollected()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // 注意：不手动注册 AttributedDiscoveredDefinitions——验证仅靠 [NotificationDefinitionProvider] 反射扫描被发现
        new NotificationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        using var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<INotificationDefinitionManager>();

        // 反射扫描注册的 Provider 被 DefinitionManager 收集
        Assert.True(manager.Exists(AttributedDiscoveredDefinitions.AttributedAlert));
    }

    /// <summary>测试专用特性发现 Provider——标注 [NotificationDefinitionProvider]，仅靠反射扫描注册（M3）。</summary>
    [NotificationDefinitionProvider]
    private sealed class AttributedDiscoveredDefinitions : INotificationDefinitionProvider
    {
        public const string AttributedAlert = "AttributedAlert";

        public void Define(INotificationDefinitionContext context)
            => context.Add(new NotificationDefinition(AttributedAlert, "特性发现公告"));
    }

    /// <summary>测试专用重复定义 Provider——与 <see cref="TestNotificationDefinitions"/> 同名定义，用于重复校验测试。</summary>
    private sealed class DuplicateOrderShippedDefinitions : INotificationDefinitionProvider
    {
        public void Define(INotificationDefinitionContext context)
            => context.Add(new NotificationDefinition(TestNotificationDefinitions.OrderShipped, "重复的订单已发货"));
    }
}
