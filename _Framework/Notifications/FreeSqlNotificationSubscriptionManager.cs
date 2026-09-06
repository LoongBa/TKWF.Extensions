using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;

namespace TKWF.Ext.Notifications;

/// <summary>
/// FreeSql 订阅管理实现（internal sealed，异常静默对齐既有扩展）。
/// <para>订阅幂等：先查重（UserId+Name+EntityType+EntityId），存在则跳过——DB 唯一约束兜底（m1）。</para>
/// </summary>
internal sealed class FreeSqlNotificationSubscriptionManager : INotificationSubscriptionManager
{
    private readonly IFreeSql _freeSql;

    public FreeSqlNotificationSubscriptionManager(IFreeSql freeSql)
        => _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));

    public async Task SubscribeAsync(long userId, string notificationName, CancellationToken ct = default)
        => await SubscribeCoreAsync(userId, notificationName, null, null, ct);

    public async Task SubscribeAsync(long userId, string notificationName, string entityTypeName, string entityId, CancellationToken ct = default)
        => await SubscribeCoreAsync(userId, notificationName, entityTypeName, entityId, ct);

    private async Task SubscribeCoreAsync(
        long userId, string notificationName, string? entityTypeName, string? entityId, CancellationToken ct)
    {
        try
        {
            var exists = await _freeSql.Select<NotificationSubscriptionEntity>()
                .Where(s => s.UserId == userId
                    && s.NotificationName == notificationName
                    && s.EntityTypeName == entityTypeName
                    && s.EntityId == entityId)
                .AnyAsync(ct);
            if (exists) return; // 幂等：已订阅跳过

            await _freeSql.Insert(new NotificationSubscriptionEntity
            {
                UserId = userId,
                NotificationName = notificationName,
                EntityTypeName = entityTypeName,
                EntityId = entityId,
                CreateTime = DateTime.UtcNow
            }).ExecuteAffrowsAsync(ct);
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
            await _freeSql.Delete<NotificationSubscriptionEntity>()
                .Where(s => s.UserId == userId && s.NotificationName == notificationName)
                .ExecuteAffrowsAsync(ct);
        }
        catch (Exception)
        {
            // 异常静默
        }
    }
}