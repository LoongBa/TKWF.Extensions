using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Notifications.DTOs;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 用户通知视图查询服务（V0.5.0 REST 直接暴露）——VEntity DTO 一等公民。
/// <para>机制事实（方案 03 探针）：VEntity 手写只读 DataService 不支持 REST（ControllerGenerator
/// <c>isDataService</c> 命名门控 early-return）——本服务采用非 DataService 命名的 Service 包装类
/// （<c>[GenerateController]</c> Service 模式，public async 方法自动纳入契约），注入 VEntity DataService 委托。</para>
/// <para>⚠️ 安全守卫（oracle3 C1-high/C5）：userId 从 <see cref="IDomainUser"/> 取当前用户——仅本人，
/// 不信任客户端传参（防 IDOR 水平越权）；管理员全量查询另设 <c>[RequirePermission]</c> 守卫端点（v0.1.0 不做，留待管理需求）。</para>
/// <para>注册：消费方 SG1b 自动注册（MetaType.Service → AddService，经 <c>User.Use&lt;T&gt;()</c> AOP 解析）——
/// 对齐 <see cref="NotificationsExtensionInitializer{TUserInfo}"/> 内既有 Service 形态（不补 TryAddScoped）。</para>
/// </summary>
[GenerateController]
public partial class UserNotificationViewQueryService(IDomainUser user, UserNotificationViewDataService viewDataService)
    : DomainServiceBase(user)
{
    /// <summary>取当前用户收件箱（VEntity DTO 一等公民——含 Name/Severity/DisplayName 完整字段）。
    /// ⚠️ 仅本人（userId 从 <see cref="IDomainUser"/> 解析）；name 可空——null = 查全部收件箱
    /// （对齐门面 <c>NotificationStore.GetListAsync</c> 无 name 语义，REST 面 ≥ 门面面）。</summary>
    [ApiExpose(RestRoute = "/api/notifications/inbox", RestMethod = "GET")]
    public async Task<List<UserNotificationViewDto>> GetMyInboxAsync(
        int page, int pageSize, string? name = null, CancellationToken ct = default)
    {
        var userId = ResolveCurrentUserId();
        // name 可空（oracle3 C4-med/C7）：null = 查全部收件箱。两路径均走 VEntity 视图——
        // JOIN 携带列（Name/Severity/DisplayName）不再丢弃（方案 03 目标 1 完整落地）。
        var views = name is null
            ? await viewDataService.GetPagedAsync(userId, page, pageSize, ct)
            : await viewDataService.GetPagedByNameAsync(userId, page, pageSize, name, ct);
        return views.Select(UserNotificationViewDto.FromEntity).ToList();
    }

    /// <summary>解析当前用户 id（<see cref="IDomainUser.UserId"/> string → long）。</summary>
    private long ResolveCurrentUserId()
        => long.TryParse(User.UserId, out var id) ? id : throw new UnauthorizedAccessException("当前用户上下文无效");
}
