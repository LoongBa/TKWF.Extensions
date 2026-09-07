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

partial class UserNotificationEntityDataService(IDomainUser user, IEntityDAC<UserNotificationEntity> dac)
    : DomainDataServiceBase<UserNotificationEntity, UserNotificationEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线整改（2026-09-07）：NotificationStore/InboxNotifier 委托路径的业务方法 ──

    /// <summary>获取用户未读通知（按创建时间倒序）。</summary>
    public async Task<List<UserNotificationEntity>> GetUnreadByUserIdAsync(long userId, CancellationToken ct = default)
        => await EntitySelectAsync(
            n => n.UserId == userId && n.State == 0,
            0, 1000, q => q.OrderByDescending(n => n.CreateTime), ct);

    /// <summary>分页获取用户通知（按创建时间倒序）。</summary>
    public async Task<List<UserNotificationEntity>> GetListPagedByUserIdAsync(
        long userId, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        return await EntitySelectAsync(
            n => n.UserId == userId,
            (page - 1) * pageSize, pageSize,
            q => q.OrderByDescending(n => n.CreateTime), ct);
    }

    /// <summary>按通知名过滤分页（跨表两步：先经 Notification DataService 解析匹配 Id 集合，再查 UserNotification）。
    /// <para>解决单实体 DAC 无法跨表 Join 的限制——由调用方 Store 注入 NotificationEntityDataService。</para>
    /// <para>适用范围：通知名为稳定标识符（如 "OrderCreated"），单名发布量通常低（千级内）——两步查询内存开销可忽略。
    /// 若单名发布量达万级，应考虑 VEntity（见 v4.9.102 VEntity 方案：跨表 JOIN 单查询下推 DB，但需 Service 包装 + 生产建视图）。</para></summary>
    public async Task<List<UserNotificationEntity>> GetListPagedByNameAsync(
        long userId, int page, int pageSize, string name, NotificationEntityDataService notificationDataService, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;

        // 第一步：按 name 投影取 Notification.Id 集合（SQL 级投影仅取 Id——避免拉取 DataJson 等大字段到内存）
        var ids = await notificationDataService.SelectAsync(
            n => n.Id,
            predicate: n => n.Name == name,
            limit: int.MaxValue,
            ct: ct);
        if (ids.Count == 0) return new List<UserNotificationEntity>();

        // 第二步：按 Id 集合过滤 UserNotification
        return await EntitySelectAsync(
            n => n.UserId == userId && ids.Contains(n.NotificationId),
            (page - 1) * pageSize, pageSize,
            q => q.OrderByDescending(n => n.CreateTime), ct);
    }

    /// <summary>未读计数。</summary>
    public async Task<long> CountUnreadByUserIdAsync(long userId, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(n => n.UserId == userId && n.State == 0, 0, int.MaxValue, ct: ct);
        return list.Count;
    }

    /// <summary>标记单条已读（UserId + NotificationId 定位）。</summary>
    public async Task MarkReadAsync(long userId, long notificationId, DateTime readTime, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(
            n => n.UserId == userId && n.NotificationId == notificationId, 0, 1, ct: ct);
        if (list.Count == 0) return;
        var row = list[0];
        row.State = 1;
        row.ReadTime = readTime;
        await EntityUpdateAsync(row, ct);
    }

    /// <summary>全部标记已读。</summary>
    public async Task MarkAllReadByUserIdAsync(long userId, DateTime readTime, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(n => n.UserId == userId && n.State == 0, 0, int.MaxValue, ct: ct);
        foreach (var row in list)
        {
            row.State = 1;
            row.ReadTime = readTime;
            await EntityUpdateAsync(row, ct); // 逐条更新——批量 UpdateBatch 对已加载实体跟踪不可靠
        }
    }

    /// <summary>收件箱行幂等检查（同 UserId+NotificationId 已投递跳过）。</summary>
    public async Task<bool> ExistsByUserIdAndNotificationAsync(long userId, long notificationId, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(
            n => n.UserId == userId && n.NotificationId == notificationId, 0, 1, ct: ct);
        return list.Count > 0;
    }

    /// <summary>创建收件箱行（InboxNotifier 投递）。</summary>
    public async Task CreateAsync(UserNotificationEntity row, CancellationToken ct = default)
        => await EntityCreateAsync(row, ct);
}