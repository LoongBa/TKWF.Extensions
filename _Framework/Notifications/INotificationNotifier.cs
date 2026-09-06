using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知投递请求——一条通知对一个收件人投递到指定通道。
/// <para>v0.1.0 仅 Inbox 通道；v0.2.0 Email/SignalR 通道复用同一请求模型（C5：通知器 owns 投递副作用）。</para>
/// </summary>
public sealed record NotificationDeliveryRequest(
    long NotificationId,
    string NotificationName,
    string? DataJson,
    NotificationSeverity Severity,
    long UserId,
    string Channel);

/// <summary>
/// 通知通道抽象——按通道投递通知。
/// <para>v0.1.0：<see cref="InboxNotifier"/>（owns UserNotification 写入）；v0.2.0：Email/SignalR 通道实现此接口。</para>
/// <para>异常语义（M1 修订）：<b>Inbox 通道</b>参与发布事务（C4）——写入失败必须传播异常触发回滚；
/// <b>外部通道</b>（v0.2.0 Email/SignalR）best-effort——投递失败自行处理不重抛阻塞发布流程。</para>
/// </summary>
public interface INotificationNotifier
{
    /// <summary>通道名（"Inbox"/"Email"/"SignalR"）。</summary>
    string Name { get; }

    /// <summary>投递通知（Inbox 通道异常传播；外部通道 best-effort，见类注释）。</summary>
    Task DeliverAsync(NotificationDeliveryRequest request, CancellationToken ct = default);
}