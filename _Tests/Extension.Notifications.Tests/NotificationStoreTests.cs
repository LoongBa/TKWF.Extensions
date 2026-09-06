using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// <see cref="INotificationStore"/> 收件箱测试——经 DI 容器解析真实实现；
/// 数据经 <see cref="INotificationPublisher"/> 真实发布产生（定义校验 + InboxNotifier 落库全链路）。
/// </summary>
public class NotificationStoreTests
{
    [Fact]
    public async Task GetUnread_ReturnsOnlyUnread()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        // 发布 2 条 → 均未读
        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        var firstNotif = fsql.Select<NotificationEntity>().First();
        await publisher.PublishAsync(TestNotificationDefinitions.SystemAlert, userIds: new long[] { 1 });
        var secondNotif = fsql.Select<NotificationEntity>().Where(n => n.Id != firstNotif.Id).First();

        // 第 1 条标记已读
        await store.MarkReadAsync(1, firstNotif.Id);

        var unread = await store.GetUnreadAsync(1);
        Assert.Single(unread);
        Assert.Equal(secondNotif.Id, unread[0].NotificationId);
    }

    [Fact]
    public async Task GetList_Paged()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        for (int i = 0; i < 5; i++)
            await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });

        var page1 = await store.GetListAsync(1, page: 1, pageSize: 2);
        Assert.Equal(2, page1.Count);

        var page2 = await store.GetListAsync(1, page: 2, pageSize: 2);
        Assert.Equal(2, page2.Count);

        var page3 = await store.GetListAsync(1, page: 3, pageSize: 2);
        Assert.Single(page3);
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsCorrectCount()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        await publisher.PublishAsync(TestNotificationDefinitions.SystemAlert, userIds: new long[] { 1 });

        Assert.Equal(2, await store.GetUnreadCountAsync(1));

        var notif = fsql.Select<NotificationEntity>().First();
        await store.MarkReadAsync(1, notif.Id);

        Assert.Equal(1, await store.GetUnreadCountAsync(1));
    }

    [Fact]
    public async Task MarkRead_UpdatesState()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        var notif = fsql.Select<NotificationEntity>().First();
        var before = DateTime.UtcNow;

        await store.MarkReadAsync(1, notif.Id);

        var inboxRow = fsql.Select<UserNotificationEntity>().First();
        Assert.Equal(1, inboxRow.State);
        Assert.NotNull(inboxRow.ReadTime);
        NotificationTestHost.AssertRecent(inboxRow.ReadTime!.Value, before);
    }

    [Fact]
    public async Task MarkAllRead_MarksAll()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        await publisher.PublishAsync(TestNotificationDefinitions.SystemAlert, userIds: new long[] { 1 });
        Assert.Equal(2, await store.GetUnreadCountAsync(1));

        await store.MarkAllReadAsync(1);

        Assert.Equal(0, await store.GetUnreadCountAsync(1));
        var rows = fsql.Select<UserNotificationEntity>().ToList();
        Assert.All(rows, r => Assert.Equal(1, r.State));
    }

    [Fact]
    public async Task GetList_FilterByName()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var publisher = sp.GetRequiredService<INotificationPublisher>();
        var store = sp.GetRequiredService<INotificationStore>();

        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        await publisher.PublishAsync(TestNotificationDefinitions.OrderShipped, userIds: new long[] { 1 });
        await publisher.PublishAsync(TestNotificationDefinitions.SystemAlert, userIds: new long[] { 1 });

        var shipped = await store.GetListAsync(1, page: 1, pageSize: 10, name: TestNotificationDefinitions.OrderShipped);
        Assert.Equal(2, shipped.Count);

        var alert = await store.GetListAsync(1, page: 1, pageSize: 10, name: TestNotificationDefinitions.SystemAlert);
        Assert.Single(alert);
    }
}
