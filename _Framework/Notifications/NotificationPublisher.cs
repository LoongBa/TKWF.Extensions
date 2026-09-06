using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Notifications
{
    /// <summary>
    /// 通知发布器实现。
    /// <para>流程：定义校验 → 发布方权限门控（C1）→ 收件人解析 → 事务写入（C4：Notification + inbox 行原子提交）→
    /// 通道投递（C5：InboxNotifier owns UserNotification 写入）。</para>
    /// <para>数据访问红线整改（2026-09-07）：经 <see cref="NotificationEntityDataService"/> +
    /// <see cref="NotificationSubscriptionEntityDataService"/>（SG1 DataService）委托持久化——不直接注入 IFreeSql。</para>
    /// </summary>
    internal sealed class NotificationPublisher : INotificationPublisher
    {
        private readonly INotificationDefinitionManager _definitionManager;
        private readonly NotificationEntityDataService _notificationDataService;
        private readonly NotificationSubscriptionEntityDataService _subscriptionDataService;
        private readonly IEnumerable<INotificationNotifier> _notifiers;
        private readonly ITransactionManager _transactionManager;
        private readonly IServiceProvider _serviceProvider;

        public NotificationPublisher(
            INotificationDefinitionManager definitionManager,
            NotificationEntityDataService notificationDataService,
            NotificationSubscriptionEntityDataService subscriptionDataService,
            IEnumerable<INotificationNotifier> notifiers,
            ITransactionManager transactionManager,
            IServiceProvider serviceProvider)
        {
            _definitionManager = definitionManager ?? throw new ArgumentNullException(nameof(definitionManager));
            _notificationDataService = notificationDataService ?? throw new ArgumentNullException(nameof(notificationDataService));
            _subscriptionDataService = subscriptionDataService ?? throw new ArgumentNullException(nameof(subscriptionDataService));
            _notifiers = notifiers ?? throw new ArgumentNullException(nameof(notifiers));
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public async Task PublishAsync(
            string notificationName,
            NotificationData? data = null,
            NotificationSeverity severity = NotificationSeverity.Info,
            long[]? userIds = null,
            long[]? excludedUserIds = null,
            string? entityTypeName = null,
            string? entityId = null,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(notificationName);

            // 1. 定义校验（快速失败）
            var definition = _definitionManager.Get(notificationName);

            // 2. 发布方权限门控（C1：仅校验发布方当前用户）
            if (definition.PermissionName != null)
                await EnsurePublisherHasPermissionAsync(notificationName, definition.PermissionName, ct);

            // 3. 收件人解析
            var recipients = await ResolveRecipientsAsync(
                notificationName, userIds, excludedUserIds, entityTypeName, entityId, ct);
            if (recipients.Count == 0)
                return; // 无收件人（含 userIds=[] 空操作）

            // 4. 事务写入（C4：Notification + 全部 inbox 行原子提交）
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                var now = DateTime.UtcNow;
                var notification = new NotificationEntity
                {
                    Name = definition.Name,
                    DisplayName = definition.DisplayName,
                    DataJson = data?.ToJson(),
                    Severity = (int)severity,
                    EntityTypeName = entityTypeName,
                    EntityId = entityId,
                    CreateTime = now
                };
                await _notificationDataService.CreateAsync(notification, ct);

                foreach (var recipientId in recipients)
                {
                    var request = new NotificationDeliveryRequest(
                        notification.Id,
                        notification.Name,
                        notification.DataJson,
                        severity,
                        recipientId,
                        "Inbox");

                    foreach (var notifier in _notifiers)
                    {
                        // TODO(v0.2.0)：按定义 UseChannels() 或请求 Channel 路由多通道（Email/SignalR）；
                        // v0.1.0 仅 Inbox 通道，硬编码过滤
                        if (notifier.Name != "Inbox") continue;
                        await notifier.DeliverAsync(request, ct);
                    }
                }

                await scope.CommitAsync(ct);
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        private async Task EnsurePublisherHasPermissionAsync(string notificationName, string permissionName, CancellationToken ct)
        {
            var checker = _serviceProvider.GetService<IPermissionChecker>();
            if (checker == null)
                throw new InvalidOperationException(
                    $"通知 {notificationName} 需要权限 {permissionName}，但 IPermissionChecker 未注册（须启用 Permissions 扩展）");

            var granted = await checker.IsGrantedAsync(permissionName);
            if (!granted)
                throw new InvalidOperationException($"发布方无权限发布通知 {notificationName}（需要权限：{permissionName}）");
        }

        private async Task<List<long>> ResolveRecipientsAsync(
            string notificationName,
            long[]? userIds,
            long[]? excludedUserIds,
            string? entityTypeName,
            string? entityId,
            CancellationToken ct)
        {
            var excluded = excludedUserIds == null
                ? new HashSet<long>()
                : new HashSet<long>(excludedUserIds);

            List<long> recipients;
            if (userIds != null)
            {
                // 显式收件人：[]=空操作，[x,y]=去重+排除
                if (userIds.Length == 0)
                    return new List<long>();
                recipients = userIds.Distinct().Where(id => !excluded.Contains(id)).ToList();
            }
            else
            {
                // 按订阅者解析（M2 修订：并集语义——定义级订阅者收全部，实体级订阅者收匹配实体）
                var subscribers = await _subscriptionDataService.GetSubscriberUserIdsAsync(
                    notificationName, entityTypeName, entityId, ct);
                recipients = subscribers.Where(id => !excluded.Contains(id)).ToList();
            }

            return recipients;
        }
    }
}