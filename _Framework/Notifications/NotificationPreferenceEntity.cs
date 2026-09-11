using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Notifications;

/// <summary>
/// V0.3.0：通知偏好实体——用户对特定通知的通道偏好（覆盖定义级 UseChannels）。
/// <para>ChannelsJson 存通道列表 JSON（如 <c>["Email"]</c>；null = 回退定义级）。
/// <c>UX_NotificationPreference_User_Notification</c>（UserId+NotificationName）唯一——每用户每通知至多一条偏好。</para>
/// </summary>
[Table("NotificationPreference")]
[FreeSql.DataAnnotations.Index("UX_NotificationPreference_User_Notification",
    nameof(UserId) + "," + nameof(NotificationName), IsUnique = true)]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class NotificationPreferenceEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>偏好用户 ID。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long UserId { get; set; }

    /// <summary>偏好通知名（对齐 Notification.Name）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(128)]
    public string NotificationName { get; set; } = "";

    /// <summary>通道列表 JSON（如 ["Email"]；null = 回退定义级——P2-3 统一 JSON 存储）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true)]
    [MaxLength(256)]
    public string? ChannelsJson { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>更新时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}