using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知订阅管理——用户按通知名订阅（定义级或实体级）。
/// </summary>
public interface INotificationSubscriptionManager
{
    /// <summary>定义级订阅——关注该通知全部发布（幂等）。</summary>
    Task SubscribeAsync(long userId, string notificationName, CancellationToken ct = default);

    /// <summary>实体级订阅——只关注某个具体实体实例（幂等）。</summary>
    Task SubscribeAsync(long userId, string notificationName, string entityTypeName, string entityId, CancellationToken ct = default);

    /// <summary>退订（定义级——删除该用户该通知的全部订阅，含实体级）。</summary>
    Task UnsubscribeAsync(long userId, string notificationName, CancellationToken ct = default);
}