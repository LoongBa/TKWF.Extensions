using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Notifications
{
    /// <summary>
    /// 收件箱存储实现——经 <see cref="UserNotificationEntityDataService"/> + <see cref="UserNotificationViewDataService"/>
    /// （SG1 DataService + VEntity 手写只读 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：不直接注入 IFreeSql。
    /// <para>V0.2.0：按通知名过滤改用 <see cref="UserNotificationViewDataService"/>（VEntity JOIN 单查询，替代两步查询）。</para>
    /// <para>异常静默对齐既有扩展：查询失败返回空/0，写入失败记录 Warning。</para>
    /// <para>V4.10.53（领域自治根治，ADR90 正确路线）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c>
    /// 获取用户上下文（IDomainUser 永不注册 DI——D01 领域自治，旧 TryAddScoped 构造注入 IDomainUser 生产必失败）；
    /// DataService 经 <c>User.Use&lt;XxxDataService&gt;()</c> NoAop 懒加载（DI004 零豁免）。注册形态改
    /// <c>AddConstructibleService&lt;INotificationStore, NotificationStore&gt;</c>（接口可构造守卫工厂 + 实现类 throw-factory）。
    /// <b>命名重组候选（本批不改）</b>：NotificationStore 是组合门面 + 降级矩阵，命名重组
    /// NotificationStore → NotificationInboxManager 留待后续批次。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class NotificationStore : DomainServiceBase, INotificationStore
    {
        private UserNotificationEntityDataService? _userNotificationDataService;
        private UserNotificationViewDataService? _userNotificationViewDataService;
        private readonly ILogger<NotificationStore> _logger;

        private UserNotificationEntityDataService UserNotificationDataService => _userNotificationDataService ??= User.Use<UserNotificationEntityDataService>();
        private UserNotificationViewDataService UserNotificationViewDataService => _userNotificationViewDataService ??= User.Use<UserNotificationViewDataService>();

        public NotificationStore(
            IDomainUser user,
            ILogger<NotificationStore> logger)
            : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<UserNotificationEntity>> GetUnreadAsync(long userId, CancellationToken ct = default)
        {
            try { return await UserNotificationDataService.GetUnreadByUserIdAsync(userId, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "未读通知查询失败: UserId={UserId}", userId); return Array.Empty<UserNotificationEntity>(); }
        }

        public async Task<IReadOnlyList<UserNotificationEntity>> GetListAsync(
            long userId, int page, int pageSize, string? name = null, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrEmpty(name))
                    return await UserNotificationDataService.GetListPagedByUserIdAsync(userId, page, pageSize, ct);
                // V0.2.0 VEntity：按通知名过滤跨表 JOIN 单查询（UserNotification → Notification），替代两步查询。
                // 视图行映射回 UserNotificationEntity，接口签名不变；Name/Severity/DisplayName 由 GraphQL 路径消费。
                var views = await UserNotificationViewDataService.GetPagedByNameAsync(userId, page, pageSize, name, ct);
                return views.Select(v => new UserNotificationEntity
                {
                    Id = v.Id,
                    UserId = v.UserId,
                    NotificationId = v.NotificationId,
                    State = v.State,
                    ReadTime = v.ReadTime,
                    CreateTime = v.CreateTime
                }).ToList();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "通知列表查询失败: UserId={UserId}", userId); return Array.Empty<UserNotificationEntity>(); }
        }

        public async Task<int> GetUnreadCountAsync(long userId, CancellationToken ct = default)
        {
            try { return (int)await UserNotificationDataService.CountUnreadByUserIdAsync(userId, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "未读计数失败: UserId={UserId}", userId); return 0; }
        }

        public async Task MarkReadAsync(long userId, long notificationId, CancellationToken ct = default)
        {
            try { await UserNotificationDataService.MarkReadAsync(userId, notificationId, DateTime.UtcNow, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "标记已读失败: UserId={UserId}, NotificationId={NotificationId}", userId, notificationId); }
        }

        public async Task MarkAllReadAsync(long userId, CancellationToken ct = default)
        {
            try { await UserNotificationDataService.MarkAllReadByUserIdAsync(userId, DateTime.UtcNow, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "全部标记已读失败: UserId={UserId}", userId); }
        }
    }
}