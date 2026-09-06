using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 用户通知实体——收件箱行（每收件人一条，关联 <see cref="NotificationEntity"/>）。
/// <para>State：0=Unread，1=Read。UserId 对齐 Identity/Permissions 的 long 约定（m7）。</para>
/// </summary>
[Table("UserNotification")]
[FreeSql.DataAnnotations.Index("IX_UserNotification_UserState", nameof(UserId) + "," + nameof(State), IsUnique = false)]
[FreeSql.DataAnnotations.Index("IX_UserNotification_NotificationId", nameof(NotificationId), IsUnique = false)]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class UserNotificationEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>收件人用户 ID（消费方用户 ID，long 约定）。</summary>
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

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6, CanUpdate = false)]
    public DateTime CreateTime { get; set; }
}