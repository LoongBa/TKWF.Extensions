using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Notifications
{
    /// <summary>
    /// 收件箱存储实现——经 <see cref="UserNotificationEntityDataService"/> + <see cref="UserNotificationViewDataService"/>
    /// （SG1 DataService + VEntity 手写只读 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：不直接注入 IFreeSql。
    /// <para>V0.2.0：按通知名过滤改用 <see cref="UserNotificationViewDataService"/>（VEntity JOIN 单查询，替代两步查询）。</para>
    /// <para>异常静默对齐既有扩展：查询失败返回空/0，写入失败记录 Warning。</para>
    /// </summary>
    internal sealed class NotificationStore : INotificationStore
    {
        private readonly UserNotificationEntityDataService _userNotificationDataService;
        private readonly UserNotificationViewDataService _userNotificationViewDataService;
        private readonly ILogger<NotificationStore> _logger;

        public NotificationStore(
            UserNotificationEntityDataService userNotificationDataService,
            UserNotificationViewDataService userNotificationViewDataService,
            ILogger<NotificationStore> logger)
        {
            _userNotificationDataService = userNotificationDataService ?? throw new ArgumentNullException(nameof(userNotificationDataService));
            _userNotificationViewDataService = userNotificationViewDataService ?? throw new ArgumentNullException(nameof(userNotificationViewDataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<UserNotificationEntity>> GetUnreadAsync(long userId, CancellationToken ct = default)
        {
            try { return await _userNotificationDataService.GetUnreadByUserIdAsync(userId, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "未读通知查询失败: UserId={UserId}", userId); return Array.Empty<UserNotificationEntity>(); }
        }

        public async Task<IReadOnlyList<UserNotificationEntity>> GetListAsync(
            long userId, int page, int pageSize, string? name = null, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrEmpty(name))
                    return await _userNotificationDataService.GetListPagedByUserIdAsync(userId, page, pageSize, ct);
                // V0.2.0 VEntity：按通知名过滤跨表 JOIN 单查询（UserNotification → Notification），替代两步查询。
                // 视图行映射回 UserNotificationEntity，接口签名不变；Name/Severity/DisplayName 由 GraphQL 路径消费。
                var views = await _userNotificationViewDataService.GetPagedByNameAsync(userId, page, pageSize, name, ct);
                return views.Select(v => new UserNotificationEntity
                {
                    Id = v.Id,
                    UserId = v.UserId,
                    NotificationId = v.NotificationId,
                    State = v.State,
                    ReadTime = v.ReadTime,
                    CreateTime = v.CreateTime
                }).ToList();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "通知列表查询失败: UserId={UserId}", userId); return Array.Empty<UserNotificationEntity>(); }
        }

        public async Task<int> GetUnreadCountAsync(long userId, CancellationToken ct = default)
        {
            try { return (int)await _userNotificationDataService.CountUnreadByUserIdAsync(userId, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "未读计数失败: UserId={UserId}", userId); return 0; }
        }

        public async Task MarkReadAsync(long userId, long notificationId, CancellationToken ct = default)
        {
            try { await _userNotificationDataService.MarkReadAsync(userId, notificationId, DateTime.UtcNow, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "标记已读失败: UserId={UserId}, NotificationId={NotificationId}", userId, notificationId); }
        }

        public async Task MarkAllReadAsync(long userId, CancellationToken ct = default)
        {
            try { await _userNotificationDataService.MarkAllReadByUserIdAsync(userId, DateTime.UtcNow, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "全部标记已读失败: UserId={UserId}", userId); }
        }
    }
}