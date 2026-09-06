using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知订阅实体——用户按通知名订阅（定义级或实体级）。
/// <para>EntityTypeName/EntityId 均为 null = 定义级订阅（关注该通知全部发布）；
/// 非 null = 实体级订阅（只关注某个具体实体实例）。</para>
/// </summary>
[Table("NotificationSubscription")]
[FreeSql.DataAnnotations.Index("IX_NotificationSubscription_User", nameof(UserId), IsUnique = false)]
[FreeSql.DataAnnotations.Index("UX_NotificationSubscription_Unique",
    nameof(UserId) + "," + nameof(NotificationName) + "," + nameof(EntityTypeName) + "," + nameof(EntityId),
    IsUnique = true)]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class NotificationSubscriptionEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>订阅用户 ID。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long UserId { get; set; }

    /// <summary>订阅的通知名。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(128)]
    public string NotificationName { get; set; } = "";

    /// <summary>实体级订阅：实体类型（null=定义级）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true)]
    [MaxLength(256)]
    public string? EntityTypeName { get; set; }

    /// <summary>实体级订阅：实体 ID（null=定义级）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5, IsNullable = true)]
    [MaxLength(128)]
    public string? EntityId { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6, CanUpdate = false)]
    public DateTime CreateTime { get; set; }
}