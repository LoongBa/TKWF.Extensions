using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Notifications
{
    /// <summary>
    /// 通知发布器实现。
    /// <para>流程：定义校验 → 发布方权限门控（C1）→ 收件人解析 → <b>逐用户权限门控（V0.3.0：委托
    /// <see cref="IPermissionBatchChecker"/>）</b> → <b>用户偏好覆盖（V0.3.0：事务前批量预取，Oracle P2-1）</b>
    /// → 事务写入（C4：Notification + inbox 行原子提交）→ 多通道投递（C5：按"最终通道列表"逐通道匹配 notifier；
    /// InboxNotifier owns UserNotification 写入，外部通道 best-effort M1）。</para>
    /// <para>数据访问红线（2026-09-07）：经 <see cref="NotificationEntityDataService"/> +
    /// <see cref="NotificationSubscriptionEntityDataService"/>（SG1 DataService）委托持久化——不直接注入 IFreeSql；
    /// 偏好经 <see cref="INotificationPreferenceManager"/> 门面（内部委托 DataService）。</para>
    /// </summary>
    internal sealed class NotificationPublisher : INotificationPublisher
    {
        private readonly INotificationDefinitionManager _definitionManager;
        private readonly NotificationEntityDataService _notificationDataService;
        private readonly NotificationSubscriptionEntityDataService _subscriptionDataService;
        private readonly INotificationPreferenceManager _preferenceManager;
        private readonly IEnumerable<INotificationNotifier> _notifiers;
        private readonly ITransactionManager _transactionManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NotificationPublisher> _logger;

        public NotificationPublisher(
            INotificationDefinitionManager definitionManager,
            NotificationEntityDataService notificationDataService,
            NotificationSubscriptionEntityDataService subscriptionDataService,
            INotificationPreferenceManager preferenceManager,
            IEnumerable<INotificationNotifier> notifiers,
            ITransactionManager transactionManager,
            IServiceProvider serviceProvider,
            ILogger<NotificationPublisher> logger)
        {
            _definitionManager = definitionManager ?? throw new ArgumentNullException(nameof(definitionManager));
            _notificationDataService = notificationDataService ?? throw new ArgumentNullException(nameof(notificationDataService));
            _subscriptionDataService = subscriptionDataService ?? throw new ArgumentNullException(nameof(subscriptionDataService));
            _preferenceManager = preferenceManager ?? throw new ArgumentNullException(nameof(preferenceManager));
            _notifiers = notifiers ?? throw new ArgumentNullException(nameof(notifiers));
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // 3.5 V0.3.0 逐用户权限门控——委托 Permissions IPermissionBatchChecker（P1-4：安全逻辑单一真相源）
            if (definition.PermissionName != null)
            {
                recipients = await FilterRecipientsByPermissionAsync(
                    notificationName, definition.PermissionName, recipients, ct);
                if (recipients.Count == 0)
                    return; // 全部收件人无权限
            }

            // 3.6 V0.3.0 偏好覆盖——事务前一次批量预取（P2-1：避免事务内 N 次往返）
            var preferenceMap = await _preferenceManager.GetChannelsBatchAsync(recipients, notificationName, ct);

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
                    // 多通道路由（v0.2.0 定义级 + v0.3.0 用户偏好覆盖）：按"最终通道列表"逐通道投递
                    //（C5：通知器 owns 投递副作用；Inbox 通道参与事务 C4 异常传播，外部通道 best-effort M1）
                    var channels = ResolveChannels(recipientId, preferenceMap, definition.Channels);
                    foreach (var channel in channels)
                    {
                        var request = new NotificationDeliveryRequest(
                            notification.Id,
                            notification.Name,
                            notification.DataJson,
                            severity,
                            recipientId,
                            channel);

                        foreach (var notifier in _notifiers)
                        {
                            if (notifier.Name != channel) continue;
                            await notifier.DeliverAsync(request, ct);
                        }
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

        /// <summary>
        /// V0.3.0：逐收件人权限门控——完全委托 <see cref="IPermissionBatchChecker"/>（Permissions v0.9.0）。
        /// <para>Oracle P1-4：Notifications 不直连 IPermissionStore、不定义角色解析器、不重实现角色回退——
        /// Admin.All 放行 / fail-closed / 用户→角色回退由 PermissionChecker 内部统一处理。</para>
        /// <para>Oracle P2-2：<see cref="IPermissionBatchChecker"/> 未注册（Permissions 未启用）→ 跳过权限过滤 + Warning
        /// （降级为仅发布方门控 C1——扩展纯接线语义）。</para>
        /// </summary>
        private async Task<List<long>> FilterRecipientsByPermissionAsync(
            string notificationName, string permissionName, List<long> recipients, CancellationToken ct)
        {
            var batchChecker = _serviceProvider.GetService<IPermissionBatchChecker>();
            if (batchChecker == null)
            {
                _logger.LogWarning(
                    "IPermissionBatchChecker 未注册——通知 {NotificationName} 的逐用户权限门控已跳过（降级为仅发布方门控 C1；须启用 Permissions v0.9.0+）",
                    notificationName);
                return recipients;
            }

            var grantMap = await batchChecker.IsGrantedAsync(recipients, permissionName);
            return recipients.Where(id => grantMap.TryGetValue(id, out var granted) && granted).ToList();
        }

        /// <summary>解析收件人最终通道列表——有偏好用偏好（含空列表 = 不接收，P2-2），无偏好回退定义级。</summary>
        private static IReadOnlyList<string> ResolveChannels(
            long userId,
            IReadOnlyDictionary<long, IReadOnlyList<string>?> preferenceMap,
            IReadOnlyList<string> definitionChannels)
        {
            if (preferenceMap.TryGetValue(userId, out var preferred) && preferred != null)
                return preferred;
            return definitionChannels;
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