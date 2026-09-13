using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.SignalR.Tests;

/// <summary>
/// P2-4（Oracle）：多通道联动集成测试——真实 <see cref="NotificationPublisher"/>（主包）
/// + Fake <see cref="IHubContext{NotificationsHub}"/>（SignalR 包）跨包协作。
/// <para>覆盖：<c>UseChannels("Inbox","SignalR")</c> 双通道都投递；用户偏好 <c>["SignalR"]</c>（排除 Inbox）
/// 时只推 SignalR 不写 Inbox（偏好覆盖 × 通道联动）。</para>
/// </summary>
public class NotificationSignalRIntegrationTests
{
    // ─── 双通道路由联动 ───

    [Fact]
    public async Task Publish_WithInboxAndSignalRChannels_BothDelivered()
    {
        using var fsql = SignalRTestHost.CreateInMemoryFreeSql();
        SignalRTestHost.SyncStructure(fsql);
        var hub = new FakeHubContext();

        using var sp = SignalRTestHost.Build(fsql, hub);
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        // 定义 UseChannels("Inbox","SignalR") → 收件箱行 + SignalR 推送都发生
        await publisher.PublishAsync(SignalRTestDefinitions.DualChannelNotice, userIds: new long[] { 1, 2 }, ct: TestContext.Current.CancellationToken);

        // ① SignalR 推送：2 收件人 × 1 推送 = 2 个 SendAsync 调用（用户标识 = UserId InvariantCulture）
        Assert.Equal(2, hub.Calls.Count);
        Assert.All(hub.Calls, c => Assert.Equal("notificationReceived", c.Method));
        Assert.Equal(new[] { "1", "2" }, hub.Calls.Select(c => c.UserIdentifier).OrderBy(x => x));

        // ② Inbox 收件箱行：2 收件人写入（经 NotificationStore 查询验证）
        var store = sp.GetRequiredService<INotificationStore>();
        var unread1 = await store.GetUnreadCountAsync(1, TestContext.Current.CancellationToken);
        var unread2 = await store.GetUnreadCountAsync(2, TestContext.Current.CancellationToken);
        Assert.True(unread1 >= 1, "收件箱应有用户 1 的未读行");
        Assert.True(unread2 >= 1, "收件箱应有用户 2 的未读行");
    }

    // ─── 偏好覆盖 × 通道联动 ───

    [Fact]
    public async Task Publish_WithPreferenceSignalROnly_SignalRDelivered_InboxSkipped()
    {
        using var fsql = SignalRTestHost.CreateInMemoryFreeSql();
        SignalRTestHost.SyncStructure(fsql);
        var hub = new FakeHubContext();

        using var sp = SignalRTestHost.Build(fsql, hub);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var preference = sp.GetRequiredService<INotificationPreferenceManager>();

        // 用户 1 偏好 ["SignalR"]（排除 Inbox）→ 双通道定义下只推 SignalR 不写收件箱
        await preference.SetAsync(1, SignalRTestDefinitions.DualChannelNotice, new[] { "SignalR" }, TestContext.Current.CancellationToken);

        await publisher.PublishAsync(SignalRTestDefinitions.DualChannelNotice, userIds: new long[] { 1, 2 }, ct: TestContext.Current.CancellationToken);

        // ① SignalR：用户 1（偏好覆盖）+ 用户 2（无偏好回退定义级 → 双通道）都在——2 次推送
        Assert.Equal(2, hub.Calls.Count);
        Assert.Equal(new[] { "1", "2" }, hub.Calls.Select(c => c.UserIdentifier).OrderBy(x => x));

        // ② Inbox：用户 1 被偏好排除（无收件箱行）；用户 2 无偏好 → 走定义级双通道 → 有收件箱行
        var store = sp.GetRequiredService<INotificationStore>();
        var unread1 = await store.GetUnreadCountAsync(1, TestContext.Current.CancellationToken);
        var unread2 = await store.GetUnreadCountAsync(2, TestContext.Current.CancellationToken);
        Assert.Equal(0, unread1);   // 偏好 ["SignalR"] 排除 Inbox
        Assert.True(unread2 >= 1, "用户 2 无偏好回退定义级 → 应写收件箱");
    }

    // ─── SignalR 单通道基线（无 Inbox）───

    [Fact]
    public async Task Publish_SignalROnlyChannel_InboxNeverWritten()
    {
        using var fsql = SignalRTestHost.CreateInMemoryFreeSql();
        SignalRTestHost.SyncStructure(fsql);
        var hub = new FakeHubContext();

        using var sp = SignalRTestHost.Build(fsql, hub);
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        // 定义 UseChannels("SignalR")（无 Inbox）→ 只推 SignalR，收件箱无行
        await publisher.PublishAsync(SignalRTestDefinitions.SignalROnlyNotice, userIds: new long[] { 1 }, ct: TestContext.Current.CancellationToken);

        Assert.Single(hub.Calls);
        Assert.Equal("1", hub.Calls[0].UserIdentifier);

        var store = sp.GetRequiredService<INotificationStore>();
        var unread = await store.GetUnreadCountAsync(1, TestContext.Current.CancellationToken);
        Assert.Equal(0, unread);   // 单通道定义下收件箱不写
    }
}
