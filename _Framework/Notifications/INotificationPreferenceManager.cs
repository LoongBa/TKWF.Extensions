using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Notifications;

/// <summary>
/// V0.3.0：通知偏好管理——用户对特定通知的通道偏好（覆盖定义级 UseChannels）。
/// <para>偏好语义：有偏好 → 用偏好 Channels（可为空=不接收该通知）；无偏好 → 回退定义级 Channels。</para>
/// </summary>
public interface INotificationPreferenceManager
{
    /// <summary>查询用户某通知的偏好通道（无偏好返回 null——回退定义级；偏好为空列表 = 不接收该通知）。</summary>
    Task<IReadOnlyList<string>?> GetChannelsAsync(long userId, string notificationName, CancellationToken ct = default);

    /// <summary>设置用户偏好通道（覆盖定义级；空列表 = 不接收该通知）。</summary>
    Task SetAsync(long userId, string notificationName, IReadOnlyList<string> channels, CancellationToken ct = default);

    /// <summary>清除用户偏好（回退定义级）。</summary>
    Task ClearAsync(long userId, string notificationName, CancellationToken ct = default);

    /// <summary>批量查询偏好通道（Oracle P2-1——收件人解析后事务前一次预取，返回 userId → 通道列表；无偏好返回 null）。</summary>
    Task<IReadOnlyDictionary<long, IReadOnlyList<string>?>> GetChannelsBatchAsync(
        IReadOnlyList<long> userIds, string notificationName, CancellationToken ct = default);
}