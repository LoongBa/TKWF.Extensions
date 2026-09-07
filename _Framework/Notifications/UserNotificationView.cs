using System;
using FreeSql.DataAnnotations;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 用户通知视图实体（VEntity）——跨表 JOIN <c>UserNotification</c> → <c>Notification</c>，按 UserId + 通知名单查询分页。
/// <para>V0.2.0：替代两步查询（先按 name 取 Notification.Id 集合，再按集合过滤收件箱）——JOIN 下推 DB，
/// 单查询完成跨表过滤 + 分页 + 排序，顺带返回通知名/严重级别/显示名（GraphQL 路径可用）。
/// VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。</para>
/// </summary>
[Table(Name = "vw_UserNotificationView", DisableSyncStructure = true)]
[DomainGenerateCode(IsView = true,
    ViewSql = @"CREATE OR REPLACE VIEW ""vw_UserNotificationView"" AS
SELECT un.""Id"", un.""UserId"", un.""NotificationId"", un.""State"", un.""ReadTime"", un.""CreateTime"",
       n.""Name"", n.""Severity"", n.""DisplayName""
FROM ""UserNotification"" un
INNER JOIN ""Notification"" n ON un.""NotificationId"" = n.""Id""",
    ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""vw_UserNotificationView"" AS
SELECT un.""Id"", un.""UserId"", un.""NotificationId"", un.""State"", un.""ReadTime"", un.""CreateTime"",
       n.""Name"", n.""Severity"", n.""DisplayName""
FROM ""UserNotification"" un
INNER JOIN ""Notification"" n ON un.""NotificationId"" = n.""Id""",
    ExposeGraphqlQuery = true,
    DefaultPageSize = 50)]
public partial class UserNotificationView
{
    /// <summary>主键——透传 UserNotification.Id（每收件箱行一条，唯一稳定）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>收件人用户 ID（外层过滤键，视图外参数化）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long UserId { get; set; }

    /// <summary>关联 Notification.Id。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    public long NotificationId { get; set; }

    /// <summary>已读状态（0=Unread，1=Read）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public int State { get; set; }

    /// <summary>已读时间（未读为 null）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5, IsNullable = true)]
    public DateTime? ReadTime { get; set; }

    /// <summary>创建时间（UTC，透传 UserNotification.CreateTime）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6, CanUpdate = false)]
    public DateTime CreateTime { get; set; }

    /// <summary>通知名（稳定唯一，外层过滤键——WHERE Name = @name）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public string Name { get; set; } = "";

    /// <summary>严重级别（NotificationSeverity 枚举值，GraphQL 路径可用）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public int Severity { get; set; }

    /// <summary>通知显示名快照（GraphQL 路径可用）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
    public string? DisplayName { get; set; }
}
