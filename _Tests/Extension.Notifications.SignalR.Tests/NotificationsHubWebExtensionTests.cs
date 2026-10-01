using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.Notifications.SignalR.Tests;

/// <summary>
/// v4.10.45 Web 装配钩子（ADR87/D22/G18）：<see cref="NotificationsHubWebExtension"/> 宿主注册验收——
/// D6 边界内聚（消费方 → 扩展）：ConfigureServices 内 AddSignalR + Options 绑定。
/// </summary>
public class NotificationsHubWebExtensionTests
{
    [Fact]
    public void ConfigureServices_Registers_SignalR_And_Options()
    {
        var services = new ServiceCollection();
        // AddSignalR 解析 IHubContext 需要 ILogger<>（DefaultHubLifetimeManager 依赖）；Options 绑定需 IConfiguration——裸集合补齐
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        new NotificationsHubWebExtension().ConfigureServices(services);

        // AddSignalR 展开：IHubContext<NotificationsHub> 可解析（D6 边界内聚——消费方零宿主注册负担；
        // AddSignalR 经开放式泛型注册，运行时解析验证）
        var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IHubContext<NotificationsHub>>());

        // Options 绑定：TKWF:Notifications:SignalR 节默认值
        var options = sp.GetRequiredService<IOptions<NotificationsSignalROptions>>().Value;
        Assert.Equal("/hubs/notifications", options.Path);
        Assert.Equal("notificationReceived", options.MethodName);
        Assert.False(options.AllowAnonymous);
    }
}
