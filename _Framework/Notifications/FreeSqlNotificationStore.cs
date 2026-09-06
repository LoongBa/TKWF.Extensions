using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;

namespace TKWF.Ext.Notifications;

/// <summary>
/// FreeSql 收件箱存储实现（internal sealed，异常静默对齐既有扩展）。
/// </summary>
internal sealed class FreeSqlNotificationStore : INotificationStore
{
    private readonly IFreeSql _freeSql;

    public FreeSqlNotificationStore(IFreeSql freeSql)
        => _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));

    public async Task<IReadOnlyList<UserNotificationEntity>> GetUnreadAsync(long userId, CancellationToken ct = default)
    {
        try
        {
            return await _freeSql.Select<UserNotificationEntity>()
                .Where(n => n.UserId == userId && n.State == 0)
                .OrderByDescending(n => n.CreateTime)
                .ToListAsync(ct);
        }
        catch (Exception)
        {
            return Array.Empty<UserNotificationEntity>();
        }
    }

    public async Task<IReadOnlyList<UserNotificationEntity>> GetListAsync(
        long userId, int page, int pageSize, string? name = null, CancellationToken ct = default)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var query = _freeSql.Select<UserNotificationEntity, NotificationEntity>()
                .InnerJoin((un, n) => un.NotificationId == n.Id)
                .Where((un, n) => un.UserId == userId);

            if (!string.IsNullOrWhiteSpace(name))
                query = query.Where((un, n) => n.Name == name);

            return await query.OrderByDescending((un, n) => un.CreateTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync((un, n) => un, ct);
        }
        catch (Exception)
        {
            return Array.Empty<UserNotificationEntity>();
        }
    }

    public async Task<int> GetUnreadCountAsync(long userId, CancellationToken ct = default)
    {
        try
        {
            return (int)await _freeSql.Select<UserNotificationEntity>()
                .Where(n => n.UserId == userId && n.State == 0)
                .CountAsync(ct);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public async Task MarkReadAsync(long userId, long notificationId, CancellationToken ct = default)
    {
        try
        {
            await _freeSql.Update<UserNotificationEntity>()
                .Set(n => n.State, 1)
                .Set(n => n.ReadTime, DateTime.UtcNow)
                .Where(n => n.UserId == userId && n.NotificationId == notificationId)
                .ExecuteAffrowsAsync(ct);
        }
        catch (Exception)
        {
            // 异常静默（对齐既有扩展模式）
        }
    }

    public async Task MarkAllReadAsync(long userId, CancellationToken ct = default)
    {
        try
        {
            await _freeSql.Update<UserNotificationEntity>()
                .Set(n => n.State, 1)
                .Set(n => n.ReadTime, DateTime.UtcNow)
                .Where(n => n.UserId == userId && n.State == 0)
                .ExecuteAffrowsAsync(ct);
        }
        catch (Exception)
        {
            // 异常静默（对齐既有扩展模式）
        }
    }
}