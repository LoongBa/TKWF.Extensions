using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// <see cref="INotificationSubscriptionManager"/> 订阅测试——定义级/实体级订阅、重复订阅幂等、退订。
/// </summary>
public class NotificationSubscriptionManagerTests
{
    [Fact]
    public async Task Subscribe_DefinitionLevel_CreatesRow()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var subManager = sp.GetRequiredService<INotificationSubscriptionManager>();

        await subManager.SubscribeAsync(1, TestNotificationDefinitions.OrderShipped);

        var row = fsql.Select<NotificationSubscriptionEntity>().First();
        Assert.Equal(1L, row.UserId);
        Assert.Equal(TestNotificationDefinitions.OrderShipped, row.NotificationName);
        Assert.Null(row.EntityTypeName);
        Assert.Null(row.EntityId);
    }

    [Fact]
    public async Task Subscribe_EntityLevel_CreatesRow()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var subManager = sp.GetRequiredService<INotificationSubscriptionManager>();

        await subManager.SubscribeAsync(1, TestNotificationDefinitions.OrderShipped, "Order", "o-100");

        var row = fsql.Select<NotificationSubscriptionEntity>().First();
        Assert.Equal(1L, row.UserId);
        Assert.Equal("Order", row.EntityTypeName);
        Assert.Equal("o-100", row.EntityId);
    }

    [Fact]
    public async Task Subscribe_Duplicate_NoDuplicateRow()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var subManager = sp.GetRequiredService<INotificationSubscriptionManager>();

        // 重复订阅（定义级）→ 幂等，仅 1 行
        await subManager.SubscribeAsync(1, TestNotificationDefinitions.OrderShipped);
        await subManager.SubscribeAsync(1, TestNotificationDefinitions.OrderShipped);

        Assert.Single(fsql.Select<NotificationSubscriptionEntity>().ToList());
    }

    [Fact]
    public async Task Unsubscribe_RemovesRow()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var subManager = sp.GetRequiredService<INotificationSubscriptionManager>();

        await subManager.SubscribeAsync(1, TestNotificationDefinitions.OrderShipped);
        Assert.Single(fsql.Select<NotificationSubscriptionEntity>().ToList());

        await subManager.UnsubscribeAsync(1, TestNotificationDefinitions.OrderShipped);

        Assert.Empty(fsql.Select<NotificationSubscriptionEntity>().ToList());
    }

    [Fact]
    public async Task Unsubscribe_NotSubscribed_NoError()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);
        using var sp = NotificationTestHost.Build(fsql);
        var subManager = sp.GetRequiredService<INotificationSubscriptionManager>();

        // 从未订阅 → 退订应静默（不抛异常）
        await subManager.UnsubscribeAsync(1, TestNotificationDefinitions.OrderShipped);

        Assert.Empty(fsql.Select<NotificationSubscriptionEntity>().ToList());
    }
}
