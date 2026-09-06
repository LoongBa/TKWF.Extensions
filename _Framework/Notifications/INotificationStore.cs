using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知收件箱存储——查询/管理用户的站内通知（读侧 + 已读状态写）。
/// </summary>
public interface INotificationStore
{
    /// <summary>获取用户的未读通知（按创建时间倒序）。</summary>
    Task<IReadOnlyList<UserNotificationEntity>> GetUnreadAsync(long userId, CancellationToken ct = default);

    /// <summary>分页获取用户通知（可选按通知名过滤，page 从 1 开始）。</summary>
    Task<IReadOnlyList<UserNotificationEntity>> GetListAsync(
        long userId, int page, int pageSize, string? name = null, CancellationToken ct = default);

    /// <summary>未读通知计数（徽标）。</summary>
    Task<int> GetUnreadCountAsync(long userId, CancellationToken ct = default);

    /// <summary>标记单条已读。</summary>
    Task MarkReadAsync(long userId, long notificationId, CancellationToken ct = default);

    /// <summary>全部标记已读。</summary>
    Task MarkAllReadAsync(long userId, CancellationToken ct = default);
}