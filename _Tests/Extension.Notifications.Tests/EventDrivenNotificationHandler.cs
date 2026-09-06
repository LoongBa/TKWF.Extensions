using System.Threading.Tasks;
using TKW.Framework.Domain.Events;

namespace TKWF.Ext.Notifications.Tests;

/// <summary>
/// 消费方领域事件示例——模拟业务模块发布的"订单创建"事件。
/// <para>仅作事件驱动接线先例（测试用），真实消费方在自己的领域项目中定义业务事件。</para>
/// </summary>
public sealed record OrderCreatedEvent(long OrderId, long BuyerId, string OrderNo);

/// <summary>
/// 事件驱动通知 handler 示例——消费方项目代码（m5：扩展只交付 INotificationPublisher sink，
/// handler 引用消费方领域事件，故在消费方/测试项目中定义）。
/// <para>[DomainEventHandler] 标记：SG 编译期扫描自动注册 + 静态派发表。</para>
/// <para>D15 post-commit 语义：handler 在业务事务提交后运行；通知写库失败 → 日志记录不重抛。</para>
/// </summary>
[DomainEventHandler]
public sealed class OrderNotificationHandler(INotificationPublisher publisher) : ILocalEventHandler<OrderCreatedEvent>
{
    public async Task HandleEventAsync(OrderCreatedEvent eventData)
    {
        await publisher.PublishAsync(
            "OrderCreated",
            new NotificationData(new Dictionary<string, object?>
            {
                ["OrderId"] = eventData.OrderId,
                ["OrderNo"] = eventData.OrderNo
            }),
            NotificationSeverity.Info,
            userIds: new long[] { eventData.BuyerId });
    }
}

/// <summary>
/// 事件驱动通知定义——注册 "OrderCreated" 通知（供 handler 发布使用）。
/// </summary>
public sealed class OrderNotificationDefinitions : INotificationDefinitionProvider
{
    public const string OrderCreated = "OrderCreated";

    public void Define(INotificationDefinitionContext context)
        => context.Add(new NotificationDefinition(OrderCreated, "订单已创建", NotificationSeverity.Info));
}