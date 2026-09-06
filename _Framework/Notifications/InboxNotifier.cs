using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Notifications
{
    /// <summary>
    /// 收件箱通知器——owns UserNotification 行写入（C5 修订）。
    /// <para>投递幂等：同 UserId+NotificationId 已存在则跳过（重复发布不产生重复 inbox 行）。</para>
    /// <para>M1 修订：不吞异常——Inbox 通道是发布事务的一部分（C4），写入失败必须传播
    /// 触发发布器回滚。经 <see cref="UserNotificationEntityDataService"/>（SG1 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）。</para>
    /// </summary>
    internal sealed class InboxNotifier : INotificationNotifier
    {
        private readonly UserNotificationEntityDataService _dataService;

        public InboxNotifier(UserNotificationEntityDataService dataService)
            => _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));

        /// <summary>通道名。</summary>
        public string Name => "Inbox";

        public async Task DeliverAsync(NotificationDeliveryRequest request, CancellationToken ct = default)
        {
            var exists = await _dataService.ExistsByUserIdAndNotificationAsync(request.UserId, request.NotificationId, ct);
            if (exists) return; // 幂等：已投递跳过

            await _dataService.CreateAsync(new UserNotificationEntity
            {
                UserId = request.UserId,
                NotificationId = request.NotificationId,
                State = 0,
                CreateTime = DateTime.UtcNow
            }, ct);
        }
    }
}