using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Events;
using TKWF.Ext.Testing.Shared;

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
/// <para>V4.10.53（领域自治根治）：<see cref="INotificationPublisher"/>（AddConstructibleService 守卫门面）
/// **不构造注入**（DI004 编译期门控禁止）——经真实 <see cref="DomainUser{TUserInfo}"/>.Use&lt;接口&gt;() 懒加载
/// （AOP 路径：设 CurrentAopUser → 守卫工厂解析；handler 由事件总线在请求作用域内派发——测试宿主绑定
/// 真实 DomainUser 后经其 Use 解析）。</para>
/// </summary>
[DomainEventHandler]
public sealed class OrderNotificationHandler(DomainUser<TestUserInfo> user) : ILocalEventHandler<OrderCreatedEvent>
{
    private INotificationPublisher? _publisher;
    private INotificationPublisher Publisher => _publisher ??= user.Use<INotificationPublisher>();

    public async Task HandleEventAsync(OrderCreatedEvent eventData)
    {
        await Publisher.PublishAsync(
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