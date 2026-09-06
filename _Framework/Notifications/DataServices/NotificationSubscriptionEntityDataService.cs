using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Notifications;
using TKWF.Ext.Notifications.DTOs;

namespace TKWF.Ext.Notifications;

partial class NotificationSubscriptionEntityDataService(IDomainUser user, IEntityDAC<NotificationSubscriptionEntity> dac)
    : DomainDataServiceBase<NotificationSubscriptionEntity, NotificationSubscriptionEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线整改（2026-09-07）：SubscriptionManager/Publisher 委托路径的业务方法 ──

    /// <summary>按通知名解析订阅者 UserId（定义级 + 实体级并集——entityTypeName 非空时含定义级订阅者）。</summary>
    public async Task<List<long>> GetSubscriberUserIdsAsync(
        string notificationName, string? entityTypeName, string? entityId, CancellationToken ct = default)
    {
        var subscriptions = await EntitySelectAsync(
            s => s.NotificationName == notificationName &&
                 (s.EntityTypeName == null ||
                  (entityTypeName != null && s.EntityTypeName == entityTypeName && s.EntityId == entityId)),
            0, 10000, ct: ct);
        return subscriptions.Select(s => s.UserId).Distinct().ToList();
    }

    /// <summary>检查订阅是否存在（幂等——4 字段完全匹配）。</summary>
    public async Task<bool> ExistsAsync(long userId, string notificationName, string? entityTypeName, string? entityId, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(
            s => s.UserId == userId && s.NotificationName == notificationName &&
                 s.EntityTypeName == entityTypeName && s.EntityId == entityId,
            0, 1, ct: ct);
        return list.Count > 0;
    }

    /// <summary>创建订阅（幂等调用方先查 ExistsAsync）。</summary>
    public async Task CreateAsync(NotificationSubscriptionEntity subscription, CancellationToken ct = default)
        => await EntityCreateAsync(subscription, ct);

    /// <summary>按用户 + 通知名删除全部订阅（定义级 + 实体级——UnsubscribeAsync 语义）。</summary>
    public async Task DeleteByUserAndNameAsync(long userId, string notificationName, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(
            s => s.UserId == userId && s.NotificationName == notificationName, 0, 10000, ct: ct);
        if (list.Count > 0)
            await EntityDeleteBatchAsync(list.Select(s => s.Id), ct);
    }
}
