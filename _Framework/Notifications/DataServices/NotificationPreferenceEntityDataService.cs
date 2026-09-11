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

/// <summary>通知偏好 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>V0.3.0：偏好表（UserId + NotificationName 唯一）——用户通道偏好覆盖定义级 UseChannels。</para></summary>
partial class NotificationPreferenceEntityDataService(IDomainUser user, IEntityDAC<NotificationPreferenceEntity> dac)
    : DomainDataServiceBase<NotificationPreferenceEntity, NotificationPreferenceEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线（2026-09-07）：PreferenceManager 委托路径的业务方法 ──

    /// <summary>按用户 + 通知名查询偏好（无偏好返回 null）。</summary>
    public async Task<NotificationPreferenceEntity?> GetByUserAndNameAsync(
        long userId, string notificationName, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(
            p => p.UserId == userId && p.NotificationName == notificationName, 0, 1, ct: ct);
        return list.FirstOrDefault();
    }

    /// <summary>创建偏好（消费者先查存在性——UX 唯一约束兜底并发）。</summary>
    public async Task CreateAsync(NotificationPreferenceEntity preference, CancellationToken ct = default)
        => await EntityCreateAsync(preference, ct);

    /// <summary>更新偏好（ChannelsJson + UpdateTime）。</summary>
    public async Task UpdateAsync(NotificationPreferenceEntity preference, CancellationToken ct = default)
        => await EntityUpdateAsync(preference, ct);

    /// <summary>按用户 + 通知名删除偏好（ClearAsync 语义——回退定义级）。</summary>
    public async Task DeleteByUserAndNameAsync(long userId, string notificationName, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(
            p => p.UserId == userId && p.NotificationName == notificationName, 0, 1, ct: ct);
        foreach (var p in list)
            await EntityDeleteBatchAsync([p.Id], ct);
    }

    /// <summary>批量查询偏好通道（Oracle P2-1：收件人解析后事务前一次预取——避免事务内 N 次往返）。
    /// <para>返回 <c>userId → ChannelsJson</c>（无偏好用户不在字典）。</para></summary>
    public async Task<Dictionary<long, string?>> GetChannelsBatchAsync(
        IEnumerable<long> userIds, string notificationName, CancellationToken ct = default)
    {
        var idList = userIds as IReadOnlyCollection<long> ?? userIds.ToList();
        if (idList.Count == 0) return new Dictionary<long, string?>();

        var preferences = await EntitySelectAsync(
            p => p.NotificationName == notificationName && idList.Contains(p.UserId),
            0, 100000, ct: ct);
        return preferences.ToDictionary(p => p.UserId, p => p.ChannelsJson);
    }
}