using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知发布器——发布通知（定义校验 + 发布方权限门控 + 收件人解析 + 事务写入 + 通道投递）。
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// 发布通知。
    /// </summary>
    /// <param name="notificationName">通知名（须已注册定义）。</param>
    /// <param name="data">通知数据（可选）。</param>
    /// <param name="severity">严重级别（覆盖定义默认值）。</param>
    /// <param name="userIds">null=按订阅者派发 / []=空操作 / [x,y]=显式收件人。</param>
    /// <param name="excludedUserIds">排除收件人（去重后移除）。</param>
    /// <param name="entityTypeName">实体级通知：实体类型（匹配实体级订阅）。</param>
    /// <param name="entityId">实体级通知：实体 ID（匹配实体级订阅）。</param>
    /// <param name="ct">取消令牌。</param>
    Task PublishAsync(
        string notificationName,
        NotificationData? data = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        long[]? userIds = null,
        long[]? excludedUserIds = null,
        string? entityTypeName = null,
        string? entityId = null,
        CancellationToken ct = default);
}