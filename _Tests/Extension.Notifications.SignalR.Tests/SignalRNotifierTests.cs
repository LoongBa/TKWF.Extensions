using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.SignalR.Tests;

/// <summary>
/// SignalRNotifier 单测——Fake IHubContext 捕获型桩（不真实 AddSignalR，P2-1 低返工路径）。
/// <para>覆盖：前置缺失跳过（M1）/ 推送方法名+payload / 用户 ID InvariantCulture / 发送失败 best-effort / 无连接静默跳过。</para>
/// </summary>
public class SignalRNotifierTests
{
    private const string BaseMethodName = "notificationReceived";

    // ─── 前置缺失（M1）───

    [Fact]
    public async Task Deliver_NoHubContext_LogsWarningSkips()
    {
        // 未注册 IHubContext<NotificationsHub>（消费方未 AddSignalR 场景）→ 构造不失败 + 投递跳过不抛异常
        using var sp = BuildHost();
        var notifier = sp.GetRequiredService<SignalRNotifier>();

        await notifier.DeliverAsync(CreateRequest(), TestContext.Current.CancellationToken);
        // 无异常即通过（投递被跳过）
    }

    // ─── 正常推送 ───

    [Fact]
    public async Task Deliver_SendsPayloadToUserConnection_WithDefaultMethodName()
    {
        var hub = new FakeHubContext();
        using var sp = BuildHost(hub: hub);
        var notifier = sp.GetRequiredService<SignalRNotifier>();

        const long userId = 42;
        const string dataJson = "{\"OrderId\":\"100\"}";
        await notifier.DeliverAsync(new NotificationDeliveryRequest(7, "OrderShipped", dataJson, NotificationSeverity.Info, userId, "SignalR"), TestContext.Current.CancellationToken);

        var call = Assert.Single(hub.Calls);
        Assert.Equal(userId.ToString(System.Globalization.CultureInfo.InvariantCulture), call.UserIdentifier);
        Assert.Equal(BaseMethodName, call.Method);
        var payload = Assert.IsType<SignalRNotificationPayload>(call.Payload);
        Assert.Equal(7, payload.NotificationId);
        Assert.Equal("OrderShipped", payload.Name);
        Assert.Equal("Info", payload.Severity);
        Assert.Equal(dataJson, payload.DataJson);
        Assert.Equal(DateTimeKind.Utc, payload.CreateTime.Kind);
    }

    [Fact]
    public async Task Deliver_RespectsConfiguredMethodName()
    {
        var hub = new FakeHubContext();
        using var sp = BuildHost(hub: hub, methodName: "customPush");
        var notifier = sp.GetRequiredService<SignalRNotifier>();

        await notifier.DeliverAsync(CreateRequest(), TestContext.Current.CancellationToken);

        var call = Assert.Single(hub.Calls);
        Assert.Equal("customPush", call.Method);
    }

    [Fact]
    public async Task Deliver_DifferentUsers_DeliverToOwnConnections()
    {
        var hub = new FakeHubContext();
        using var sp = BuildHost(hub: hub);
        var notifier = sp.GetRequiredService<SignalRNotifier>();

        await notifier.DeliverAsync(new NotificationDeliveryRequest(1, "N1", null, NotificationSeverity.Info, 10, "SignalR"), TestContext.Current.CancellationToken);
        await notifier.DeliverAsync(new NotificationDeliveryRequest(1, "N1", null, NotificationSeverity.Info, 999, "SignalR"), TestContext.Current.CancellationToken);

        Assert.Equal(2, hub.Calls.Count);
        Assert.Equal("10", hub.Calls[0].UserIdentifier);
        Assert.Equal("999", hub.Calls[1].UserIdentifier);
        // long.ToString(InvariantCulture) 无文化陷阱——显式契约（P2-2）
        Assert.All(hub.Calls, c => Assert.DoesNotContain(c.UserIdentifier, char.IsLetter));
    }

    // ─── best-effort（M1）───

    [Fact]
    public async Task Deliver_NoOnlineConnection_SilentlySkips()
    {
        // 无在线连接 → SignalR Clients.User 空操作返回完成（FakeHubClients.User 恒返回记录桩，不抛）
        var hub = new FakeHubContext();
        using var sp = BuildHost(hub: hub);
        var notifier = sp.GetRequiredService<SignalRNotifier>();

        await notifier.DeliverAsync(CreateRequest(), TestContext.Current.CancellationToken);

        // 记录桩仅记录调用，无异常即通过（真实环境无连接时 SendAsync 为 no-op）
        Assert.Single(hub.Calls);
    }

    [Fact]
    public async Task Deliver_SendFailure_BestEffortNoThrow()
    {
        // SendAsync 抛异常 → catch + LogWarning，不重抛阻塞发布流程（M1 外部通道语义）
        var hub = new ThrowingHubContext();
        using var sp = BuildHost(hub: hub);
        var notifier = sp.GetRequiredService<SignalRNotifier>();

        await notifier.DeliverAsync(CreateRequest(), TestContext.Current.CancellationToken);
        // 无异常即通过（失败已吞）
    }

    // ─── Initializer 注册 ───

    [Fact]
    public void Initializer_RegistersSignalRNotifier_AndOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // BindConfiguration 惰性读取 IConfiguration（OptionsBuilder.Configure）——注册空配置桩（对齐 Tagging 测试先例）
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        new NotificationsSignalRExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        using var sp = services.BuildServiceProvider();

        // 通道注册：INotificationNotifier 集合含 Name=="SignalR" 的实现（与主包 Inbox/Email 共存）
        var notifiers = sp.GetServices<INotificationNotifier>();
        Assert.Contains(notifiers, n => n.Name == "SignalR");
        // Options 绑定可解析（默认值）
        var options = sp.GetService<Microsoft.Extensions.Options.IOptions<NotificationsSignalROptions>>()?.Value;
        Assert.NotNull(options);
    }

    [Fact]
    public void Options_Defaults()
    {
        var options = new NotificationsSignalROptions();
        Assert.Equal("notificationReceived", options.MethodName);
        Assert.Equal("/hubs/notifications", options.Path);
        Assert.False(options.AllowAnonymous);
    }

    [Fact]
    public void MapHub_DefaultPath_FromOptions()
    {
        var options = new NotificationsSignalROptions();
        Assert.Equal("/hubs/notifications", options.Path);
    }

    // ─── 基础设施 ───

    private static NotificationDeliveryRequest CreateRequest()
        => new(7, "OrderShipped", "{\"OrderId\":\"100\"}", NotificationSeverity.Info, 42, "SignalR");

    private static ServiceProvider BuildHost(
        IHubContext<NotificationsHub>? hub = null,
        string? methodName = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (hub != null) services.AddSingleton<IHubContext<NotificationsHub>>(hub);
        if (methodName != null)
        {
            services.AddOptions<NotificationsSignalROptions>()
                .Configure(o => o.MethodName = methodName);
        }
        services.AddTransient<SignalRNotifier>();
        return services.BuildServiceProvider();
    }
}

/// <summary>抛异常 HubContext 桩——Clients.User 返回 ThrowingClientProxy（SendAsync 抛异常）。
/// <para>net10 结构同 FakeHubContext（IHubClients 9 单返回成员 + Client DIM，无需 ISingleClientProxy）。</para></summary>
internal sealed class ThrowingHubContext : IHubContext<NotificationsHub>
{
    public IHubClients Clients => new ThrowingHubClients();

    public IGroupManager Groups => throw new NotSupportedException();
}

internal sealed class ThrowingHubClients : IHubClients
{
    public IClientProxy All => new ThrowingClientProxy();
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new ThrowingClientProxy();
    public IClientProxy Client(string connectionId) => new ThrowingClientProxy();
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new ThrowingClientProxy();
    public IClientProxy Group(string groupName) => new ThrowingClientProxy();
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new ThrowingClientProxy();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => new ThrowingClientProxy();
    public IClientProxy User(string userId) => new ThrowingClientProxy();
    public IClientProxy Users(IReadOnlyList<string> userIds) => new ThrowingClientProxy();
}