using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 收件箱通知器——owns UserNotification 行写入（C5 修订）。
/// <para>投递幂等：同 UserId+NotificationId 已存在则跳过（重复发布不产生重复 inbox 行）。</para>
/// <para>M1 修订：不吞异常——Inbox 通道是发布事务的一部分（C4），写入失败必须传播
/// 触发发布器回滚（Notification + 全部 inbox 行原子提交）。best-effort 语义仅适用
/// v0.2.0 外部通道（Email/SignalR），Inbox 通道例外。</para>
/// </summary>
internal sealed class InboxNotifier : INotificationNotifier
{
    private readonly IFreeSql _freeSql;

    public InboxNotifier(IFreeSql freeSql)
        => _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));

    /// <summary>通道名。</summary>
    public string Name => "Inbox";

    public async Task DeliverAsync(NotificationDeliveryRequest request, CancellationToken ct = default)
    {
        var exists = await _freeSql.Select<UserNotificationEntity>()
            .Where(n => n.UserId == request.UserId && n.NotificationId == request.NotificationId)
            .AnyAsync(ct);
        if (exists) return; // 幂等：已投递跳过

        await _freeSql.Insert(new UserNotificationEntity
        {
            UserId = request.UserId,
            NotificationId = request.NotificationId,
            State = 0,
            CreateTime = DateTime.UtcNow
        }).ExecuteAffrowsAsync(ct);
    }
}