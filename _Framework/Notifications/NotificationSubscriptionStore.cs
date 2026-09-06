using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Notifications
{
    /// <summary>
    /// 通知订阅管理实现——经 <see cref="NotificationSubscriptionEntityDataService"/>（SG1 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：不直接注入 IFreeSql。
    /// <para>异常静默对齐既有扩展；订阅幂等（查重后插——DB 唯一约束兜底 m1/M4）。</para>
    /// </summary>
    internal sealed class NotificationSubscriptionStore : INotificationSubscriptionManager
    {
        private readonly NotificationSubscriptionEntityDataService _dataService;
        private readonly ILogger<NotificationSubscriptionStore> _logger;

        public NotificationSubscriptionStore(
            NotificationSubscriptionEntityDataService dataService,
            ILogger<NotificationSubscriptionStore> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SubscribeAsync(long userId, string notificationName, CancellationToken ct = default)
            => await SubscribeCoreAsync(userId, notificationName, null, null, ct);

        public async Task SubscribeAsync(long userId, string notificationName, string entityTypeName, string entityId, CancellationToken ct = default)
            => await SubscribeCoreAsync(userId, notificationName, entityTypeName, entityId, ct);

        private async Task SubscribeCoreAsync(
            long userId, string notificationName, string? entityTypeName, string? entityId, CancellationToken ct)
        {
            try
            {
                var exists = await _dataService.ExistsAsync(userId, notificationName, entityTypeName, entityId, ct);
                if (exists) return; // 幂等：已订阅跳过

                await _dataService.CreateAsync(new NotificationSubscriptionEntity
                {
                    UserId = userId,
                    NotificationName = notificationName,
                    EntityTypeName = entityTypeName,
                    EntityId = entityId,
                    CreateTime = DateTime.UtcNow
                }, ct);
            }
            catch (Exception)
            {
                // 唯一约束冲突兜底（并发重复订阅）——静默
            }
        }

        public async Task UnsubscribeAsync(long userId, string notificationName, CancellationToken ct = default)
        {
            try
            {
                await _dataService.DeleteByUserAndNameAsync(userId, notificationName, ct);
            }
            catch (Exception)
            {
                // 异常静默
            }
        }
    }
}