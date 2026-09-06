using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Events;
using TKWF.Ext.Notifications;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// 事件驱动通知测试——验证"事件 → [DomainEventHandler] → INotificationPublisher → 站内通知"完整链路
/// （Oracle C 系列先例价值：Notifications 是第一个消费事件总线的扩展）。
/// </summary>
public class EventDrivenNotificationTests
{
    [Fact]
    public async Task OrderCreatedEvent_Handler_PublishesNotification()
    {
        using var fsql = NotificationTestHost.CreateInMemoryFreeSql();
        NotificationTestHost.SyncStructure(fsql);

        // 宿主注册：默认定义（Helpers）+ 事件通知定义（OrderCreated）+ 事件 handler
        using var sp = NotificationTestHost.Build(fsql, services =>
        {
            services.AddSingleton<INotificationDefinitionProvider, OrderNotificationDefinitions>();
            // handler 直接解析（SG 自动注册为 Transient；测试宿主手动注册）
            services.AddTransient<ILocalEventHandler<OrderCreatedEvent>, OrderNotificationHandler>();
        });

        var handler = sp.GetRequiredService<ILocalEventHandler<OrderCreatedEvent>>();
        var store = sp.GetRequiredService<INotificationStore>();

        // 事件 → handler.HandleEventAsync → 发布通知
        await handler.HandleEventAsync(new OrderCreatedEvent(OrderId: 42, BuyerId: 7, OrderNo: "ORD-42"));

        // 验证：Notification 表 1 行（OrderCreated）——无业务方法覆盖（发布态直读），保留直查
        var notification = fsql.Select<NotificationEntity>().First();
        Assert.Equal(OrderNotificationDefinitions.OrderCreated, notification.Name);
        Assert.Equal("订单已创建", notification.DisplayName);

        // 验证：收件箱 1 行（BuyerId=7，未读）——经 Store 业务方法
        var inbox = await store.GetUnreadAsync(7);
        Assert.Single(inbox);
        Assert.Equal(7L, inbox[0].UserId);
        Assert.Equal(notification.Id, inbox[0].NotificationId);

        // 验证：DataJson 可反序列化（OrderId/OrderNo 保留）
        var data = NotificationData.FromJson(notification.DataJson);
        Assert.NotNull(data);
        Assert.Equal("42", data!["OrderId"]);
        Assert.Equal("ORD-42", data["OrderNo"]);
    }
}