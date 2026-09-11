using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// V0.3.0 用户偏好路由测试——经 DI 容器解析真实 <see cref="INotificationPreferenceManager"/> + <see cref="INotificationPublisher"/>。
/// <para>覆盖：偏好 CRUD（Get/Set/Clear + 幂等更新）/ 空列表 = 不接收 / JSON 往返 / 批量预取 /
/// 发布时偏好覆盖（有偏好用偏好、无偏好回退定义级、空偏好不投递）。</para>
/// </summary>
public class NotificationPreferenceTests
{
    // ─── 偏好管理器（INotificationPreferenceManager）───

    [Fact]
    public async Task GetChannels_NoPreference_ReturnsNull()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationPreferenceManager>();

        var channels = await manager.GetChannelsAsync(1, TestNotificationDefinitions.OrderShipped);

        Assert.Null(channels); // 无偏好 → null（回退定义级）
    }

    [Fact]
    public async Task Set_ThenGet_ReturnsChannels()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationPreferenceManager>();

        await manager.SetAsync(1, TestNotificationDefinitions.OrderShipped, new[] { "Email" });

        var channels = await manager.GetChannelsAsync(1, TestNotificationDefinitions.OrderShipped);
        Assert.NotNull(channels);
        Assert.Equal(new[] { "Email" }, channels);
    }

    [Fact]
    public async Task Set_EmptyList_PreservesOptOut()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationPreferenceManager>();

        // 空列表 = 显式"不接收该通知"（区别于无偏好 null）
        await manager.SetAsync(1, TestNotificationDefinitions.OrderShipped, Array.Empty<string>());

        var channels = await manager.GetChannelsAsync(1, TestNotificationDefinitions.OrderShipped);
        Assert.NotNull(channels);   // 非 null（有偏好记录）
        Assert.Empty(channels!);    // 空列表 = 不接收
    }

    [Fact]
    public async Task Set_Twice_UpdatesSingleRow()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationPreferenceManager>();

        await manager.SetAsync(1, TestNotificationDefinitions.OrderShipped, new[] { "Email" });
        await manager.SetAsync(1, TestNotificationDefinitions.OrderShipped, new[] { "Inbox" });

        // 唯一约束（UserId + NotificationName）→ 幂等更新，不产生第二行
        Assert.Equal(1, fsql.Select<NotificationPreferenceEntity>().Count());
        var channels = await manager.GetChannelsAsync(1, TestNotificationDefinitions.OrderShipped);
        Assert.Equal(new[] { "Inbox" }, channels);
    }

    [Fact]
    public async Task Clear_AfterSet_ReturnsNull()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationPreferenceManager>();

        await manager.SetAsync(1, TestNotificationDefinitions.OrderShipped, new[] { "Email" });
        await manager.ClearAsync(1, TestNotificationDefinitions.OrderShipped);

        Assert.Null(await manager.GetChannelsAsync(1, TestNotificationDefinitions.OrderShipped));
        Assert.Equal(0, fsql.Select<NotificationPreferenceEntity>().Count());
    }

    [Fact]
    public async Task GetChannelsBatch_ReturnsMapForUsersWithPreference()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var manager = sp.GetRequiredService<INotificationPreferenceManager>();

        await manager.SetAsync(1, TestNotificationDefinitions.OrderShipped, new[] { "Email" });
        await manager.SetAsync(3, TestNotificationDefinitions.OrderShipped, Array.Empty<string>());

        var map = await manager.GetChannelsBatchAsync(new long[] { 1, 2, 3 }, TestNotificationDefinitions.OrderShipped);

        // 一次批量查询——仅返回有偏好记录的用户（无偏好用户不在字典）
        Assert.Equal(2, map.Count);
        Assert.Equal(new[] { "Email" }, map[1]);
        Assert.Empty(map[3]!);
        Assert.False(map.ContainsKey(2));
    }

    // ─── 发布时偏好覆盖 ───

    [Fact]
    public async Task Publish_NoPreference_FallsBackToDefinitionChannels()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        var fakeInbox = new FakeRecorderNotifier("Inbox");
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.RemoveAll<INotificationNotifier>();
            services.AddSingleton<INotificationNotifier>(fakeInbox);
        });
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        // 无偏好 → 回退定义级 ["Inbox"]
        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });

        Assert.Single(fakeInbox.Requests);
        Assert.Equal(1L, fakeInbox.Requests[0].UserId);
    }

    [Fact]
    public async Task Publish_EmptyPreference_NoDelivery()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        var fakeInbox = new FakeRecorderNotifier("Inbox");
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.RemoveAll<INotificationNotifier>();
            services.AddSingleton<INotificationNotifier>(fakeInbox);
        });
        var manager = sp.GetRequiredService<INotificationPreferenceManager>();
        var store = sp.GetRequiredService<INotificationStore>();
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        await manager.SetAsync(1, TestNotificationDefinitions.OrderShipped, Array.Empty<string>());

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });

        // 空偏好 = 不接收（P2-2）→ 无投递、无 inbox 行
        Assert.Empty(fakeInbox.Requests);
        Assert.Empty(await store.GetUnreadAsync(1));
    }

    [Fact]
    public async Task Publish_PreferenceOverridesDefinitionChannels()
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
        var manager = sp.GetRequiredService<INotificationPreferenceManager>();
        var publisher = sp.GetRequiredService<INotificationPublisher>();

        // 定义级 UseChannels("Inbox","Email")；用户 1 偏好 ["Email"]（排除 Inbox），用户 2 无偏好
        await manager.SetAsync(1, MultiChannelDefinitions.MultiChannelNotice, new[] { "Email" });

        await publisher.PublishAsync(MultiChannelDefinitions.MultiChannelNotice, userIds: new long[] { 1, 2 });

        // 用户 1：偏好覆盖 → 仅 Email 通道
        Assert.Single(fakeEmail.Requests, r => r.UserId == 1);
        Assert.DoesNotContain(fakeInbox.Requests, r => r.UserId == 1);
        // 用户 2：无偏好 → 定义级 Inbox + Email
        Assert.Single(fakeInbox.Requests, r => r.UserId == 2);
        Assert.Single(fakeEmail.Requests, r => r.UserId == 2);
    }
}