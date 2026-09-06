using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Notifications;
using TKWF.Ext.Notifications.DTOs;

namespace TKWF.Ext.Notifications;

partial class NotificationEntityDataService(IDomainUser user, IEntityDAC<NotificationEntity> dac)
    : DomainDataServiceBase<NotificationEntity, NotificationEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线整改（2026-09-07）：NotificationPublisher 委托路径的业务方法 ──

    /// <summary>创建通知（发布态，回写自增 Id）。</summary>
    public async Task CreateAsync(NotificationEntity notification, CancellationToken ct = default)
        => await EntityCreateAsync(notification, ct);
}
