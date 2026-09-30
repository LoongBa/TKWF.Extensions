using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Notifications;
using TKWF.Ext.Notifications.DTOs;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 用户通知视图只读 DataService（VEntity V0.2.0）——跨表 JOIN 单查询下推 DB。
/// <para>VEntity 由 xCodeGen 跳过 DataService 模板（Engine.cs L42-46），此处手写继承
/// <see cref="DomainReadOnlyDataServiceBase{TEntity, TDto}"/>（2 参数版，与扩展现有 DataService 一致）。
/// 注入 <see cref="IEntityReadOnlyDAC{TEntity}"/>（只读契约）——绝不用 IEntityDAC（FreeSqlEntityDAC 静态守卫）。
/// 不标 <c>[GenerateController(FromDataService = true)]</c>：GetListAsync 是 Store 内部能力，
/// REST 经 INotificationStore 门面 + UserNotificationViewQueryService（V0.5.0 直接暴露）消费；
/// GraphQL 经 ExposeGraphqlQuery 由 SG1b 自动生成 resolver。
/// V0.5.0：public（UserNotificationViewQueryService 公开 ctor 依赖——CS0051 修复 + 消费方可直接注入只读查询面）。</para>
/// </summary>
public partial class UserNotificationViewDataService(IDomainUser user, IEntityReadOnlyDAC<UserNotificationView> dac)
    : DomainReadOnlyDataServiceBase<UserNotificationView, UserNotificationViewDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>单查询跨表：按 UserId + 通知名分页（JOIN UserNotification → Notification 下推 DB，替代两步查询）。
    /// 顺带返回通知名/严重级别/显示名（GraphQL 路径可用）。</summary>
    public async Task<List<UserNotificationView>> GetPagedByNameAsync(
        long userId, int page, int pageSize, string name, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        return await SelectAsync(v => v,
            predicate: v => v.UserId == userId && v.Name == name,
            skip: (page - 1) * pageSize, limit: pageSize,
            orderBy: q => q.OrderByDescending(v => v.CreateTime), ct);
    }

    /// <summary>单查询跨表：按 UserId 分页（无通知名过滤——REST 收件箱 name 可空路径，V0.5.0）。
    /// 对齐门面 <c>NotificationStore.GetListAsync</c> 无 name 语义（查全部收件箱），同时保留
    /// JOIN 携带列（Name/Severity/DisplayName）——REST 面 &gt; 门面面（门面无 name 路径仍映射回实体丢弃三列）。</summary>
    public async Task<List<UserNotificationView>> GetPagedAsync(
        long userId, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        return await SelectAsync(v => v,
            predicate: v => v.UserId == userId,
            skip: (page - 1) * pageSize, limit: pageSize,
            orderBy: q => q.OrderByDescending(v => v.CreateTime), ct);
    }
}
