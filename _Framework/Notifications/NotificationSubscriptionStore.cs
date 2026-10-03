using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Notifications
{
    /// <summary>
    /// 通知订阅管理实现——经 <see cref="NotificationSubscriptionEntityDataService"/>（SG1 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：不直接注入 IFreeSql。
    /// <para>异常静默对齐既有扩展；订阅幂等（查重后插——DB 唯一约束兜底 m1/M4）。</para>
    /// <para>V4.10.53（领域自治根治，ADR90 正确路线）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c>
    /// 获取用户上下文（IDomainUser 永不注册 DI）；注册形态改
    /// <c>AddConstructibleService&lt;INotificationSubscriptionManager, NotificationSubscriptionStore&gt;</c>
    /// （接口可构造守卫工厂 + 实现类 throw-factory）。
    /// <b>命名错位候选（本批不改）</b>：类名 Store、接口 Manager——命名重组留待后续批次。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class NotificationSubscriptionStore : DomainServiceBase, INotificationSubscriptionManager
    {
        private NotificationSubscriptionEntityDataService? _dataService;
        private readonly ILogger<NotificationSubscriptionStore> _logger;

        private NotificationSubscriptionEntityDataService DataService => _dataService ??= User.Use<NotificationSubscriptionEntityDataService>();

        public NotificationSubscriptionStore(
            IDomainUser user,
            ILogger<NotificationSubscriptionStore> logger)
            : base(user)
        {
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
                var exists = await DataService.ExistsAsync(userId, notificationName, entityTypeName, entityId, ct);
                if (exists) return; // 幂等：已订阅跳过

                await DataService.CreateAsync(new NotificationSubscriptionEntity
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
                await DataService.DeleteByUserAndNameAsync(userId, notificationName, ct);
            }
            catch (Exception)
            {
                // 异常静默
            }
        }
    }
}