using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKWF.Ext.Emailing;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// V0.2.0 多通道路由 + EmailNotifier 专项测试——Fake 模式（不真实发邮件）。
/// <para>覆盖：<see cref="NotificationDefinition.UseChannels"/>（默认 Inbox / 顺序去重 / 空数组异常）、
/// <see cref="NotificationPublisher"/> 按定义通道路由（多通道全投递 / 默认 Inbox-only / 未注册通道跳过）、
/// <see cref="EmailNotifier"/> best-effort（IEmailSender 未注册跳过 / 无邮箱跳过 / 消息组装 / 发送失败不重抛）。</para>
/// </summary>
public class NotificationChannelRoutingTests
{
    // ─── NotificationDefinition.UseChannels ───

    [Fact]
    public void NotificationDefinition_UseChannels_Default_IsInbox()
    {
        var def = new NotificationDefinition("OrderShipped", "订单已发货");

        Assert.Equal(new[] { "Inbox" }, def.Channels);
    }

    [Fact]
    public void NotificationDefinition_UseChannels_SetsAndDeduplicates()
    {
        var def = new NotificationDefinition("OrderShipped", "订单已发货")
            .UseChannels("Email", "Inbox", "Email", "Inbox");

        // 按给定顺序去重存储：重复项忽略，顺序保持第一次出现
        Assert.Equal(new[] { "Email", "Inbox" }, def.Channels);
    }

    [Fact]
    public void NotificationDefinition_UseChannels_Empty_Throws()
    {
        var def = new NotificationDefinition("OrderShipped", "订单已发货");

        Assert.Throws<ArgumentException>(() => def.UseChannels());
    }

    // ─── Publisher 多通道路由 ───

    [Fact]
    public async Task Publisher_Publish_UseChannels_MultipleChannels_DeliversToAll()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        var fakeInbox = new FakeRecorderNotifier("Inbox");
        var fakeEmail = new FakeRecorderNotifier("Email");

        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.RemoveAll<INotificationNotifier>();
            services.AddSingleton<INotificationNotifier>(fakeInbox);
            services.AddSingleton<INotificationNotifier>(fakeEmail);
            services.AddSingleton<INotificationDefinitionProvider, MultiChannelDefinitions>();
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        await publisher.PublishAsync(MultiChannelDefinitions.MultiChannelNotice, userIds: new long[] { 1, 2 });

        // 定义 UseChannels("Inbox","Email") → 2 收件人 × 2 通道 = 各通知器收到 2 个请求
        Assert.Equal(2, fakeInbox.Requests.Count);
        Assert.Equal(2, fakeEmail.Requests.Count);
        Assert.All(fakeInbox.Requests, r => Assert.Equal("Inbox", r.Channel));
        Assert.All(fakeEmail.Requests, r => Assert.Equal("Email", r.Channel));
        // 同一 Notification 共享给所有通道
        Assert.Equal(fakeInbox.Requests.Select(r => r.NotificationId), fakeEmail.Requests.Select(r => r.NotificationId));
    }

    [Fact]
    public async Task Publisher_Publish_DefaultInboxOnly_OnlyInboxDelivered()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        var fakeInbox = new FakeRecorderNotifier("Inbox");
        var fakeEmail = new FakeRecorderNotifier("Email");

        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.RemoveAll<INotificationNotifier>();
            services.AddSingleton<INotificationNotifier>(fakeInbox);
            services.AddSingleton<INotificationNotifier>(fakeEmail);
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        // 不调 UseChannels → 默认 ["Inbox"] → 仅 Inbox 通道投递
        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1, 2 });

        Assert.Equal(2, fakeInbox.Requests.Count);
        Assert.Empty(fakeEmail.Requests);
    }

    [Fact]
    public async Task Publisher_Publish_UseChannels_UnknownChannel_Skipped()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        var fakeInbox = new FakeRecorderNotifier("Inbox");
        var fakeEmail = new FakeRecorderNotifier("Email");

        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.RemoveAll<INotificationNotifier>();
            services.AddSingleton<INotificationNotifier>(fakeInbox);
            services.AddSingleton<INotificationNotifier>(fakeEmail);
            services.AddSingleton<INotificationDefinitionProvider, MultiChannelDefinitions>();
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        // 定义 UseChannels("Inbox","Sms")——"Sms" 无匹配 notifier → 不抛异常、自然跳过
        await publisher.PublishAsync(MultiChannelDefinitions.UnknownChannelNotice, userIds: new long[] { 1, 2 });

        Assert.Equal(2, fakeInbox.Requests.Count);
        Assert.Empty(fakeEmail.Requests);
    }

    // ─── EmailNotifier best-effort ───

    [Fact]
    public async Task EmailNotifier_Deliver_NoEmailSender_LogsWarningSkips()
    {
        // 构造 sp 不含 IEmailSender（Emailing 扩展未启用场景）→ 前置缺失 → 不抛异常
        using var sp = BuildEmailNotifierHost();
        var notifier = sp.GetRequiredService<EmailNotifier>();

        await notifier.DeliverAsync(CreateEmailRequest());

        // 无异常即通过（投递被跳过）
    }

    [Fact]
    public async Task EmailNotifier_Deliver_UserNoEmail_Skips()
    {
        var sender = new FakeEmailSender();
        using var sp = BuildEmailNotifierHost(sender, new FakeUserEmailProvider([]));
        var notifier = sp.GetRequiredService<EmailNotifier>();

        // IUserEmailProvider 返回 null（无邮箱）→ 跳过发送
        await notifier.DeliverAsync(CreateEmailRequest());

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task EmailNotifier_Deliver_SendsEmailMessage_WithNameAndData()
    {
        var sender = new FakeEmailSender();
        using var sp = BuildEmailNotifierHost(
            sender,
            new FakeUserEmailProvider(new Dictionary<long, string?> { [42] = "buyer@example.com" }));
        var notifier = sp.GetRequiredService<EmailNotifier>();

        const string dataJson = "{\"OrderId\":\"100\",\"TrackingNo\":\"SF1234567890\"}";
        await notifier.DeliverAsync(new NotificationDeliveryRequest(7, "OrderShipped", dataJson, NotificationSeverity.Info, 42, "Email"));

        var msg = Assert.Single(sender.Sent);
        Assert.Equal("buyer@example.com", msg.To);
        Assert.Equal("OrderShipped", msg.Subject);
        Assert.Equal(dataJson, msg.Body);
    }

    [Fact]
    public async Task EmailNotifier_Deliver_SendFailure_BestEffortNoThrow()
    {
        var sender = new FakeEmailSender { ThrowOnSend = new IOException("SMTP 连接失败") };
        using var sp = BuildEmailNotifierHost(
            sender,
            new FakeUserEmailProvider(new Dictionary<long, string?> { [42] = "buyer@example.com" }));
        var notifier = sp.GetRequiredService<EmailNotifier>();

        // 外部通道 best-effort（M1）：SendAsync 抛异常 → catch + LogWarning，不重抛阻塞发布流程
        await notifier.DeliverAsync(CreateEmailRequest());

        Assert.Single(sender.Sent); // 已尝试发送
    }

    // ─── 基础设施 ───

    private static NotificationDeliveryRequest CreateEmailRequest()
        => new(7, "OrderShipped", "{\"OrderId\":\"100\"}", NotificationSeverity.Info, 42, "Email");

    private static ServiceProvider BuildEmailNotifierHost(
        FakeEmailSender? sender = null,
        FakeUserEmailProvider? emailProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (sender != null) services.AddSingleton<IEmailSender>(sender);
        if (emailProvider != null) services.AddSingleton<IUserEmailProvider>(emailProvider);
        services.AddSingleton<EmailNotifier>();
        return services.BuildServiceProvider();
    }
}

/// <summary>记录型 Notifier 桩——按 <see cref="Name"/> 匹配通道路由并记录收到的投递请求。</summary>
internal sealed class FakeRecorderNotifier : INotificationNotifier
{
    public FakeRecorderNotifier(string name) => Name = name;

    public string Name { get; }

    public List<NotificationDeliveryRequest> Requests { get; } = new();

    public Task DeliverAsync(NotificationDeliveryRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }
}

/// <summary>捕获型邮件发送桩——记录 EmailMessage，可按需抛异常模拟发送失败。</summary>
internal sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();

    public Exception? ThrowOnSend { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Sent.Add(message);
        if (ThrowOnSend != null) throw ThrowOnSend;
        return Task.CompletedTask;
    }
}

/// <summary>可配置邮箱提供者桩——按预置邮箱映射返回（未映射默认 null → 跳过发送）。</summary>
internal sealed class FakeUserEmailProvider : IUserEmailProvider
{
    private readonly Dictionary<long, string?> _emails;

    public FakeUserEmailProvider(Dictionary<long, string?> emails) => _emails = emails;

    public Task<string?> GetEmailAsync(long userId, CancellationToken ct = default)
        => Task.FromResult(_emails.GetValueOrDefault(userId));
}

/// <summary>多通道通知定义 Provider——UseChannels 声明用例（多通道 + 未注册通道）。</summary>
internal sealed class MultiChannelDefinitions : INotificationDefinitionProvider
{
    public const string MultiChannelNotice = "MultiChannelNotice";
    public const string UnknownChannelNotice = "UnknownChannelNotice";

    public void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(MultiChannelNotice, "多通道公告")
            .UseChannels("Inbox", "Email"));
        context.Add(new NotificationDefinition(UnknownChannelNotice, "未注册通道公告")
            .UseChannels("Inbox", "Sms"));
    }
}