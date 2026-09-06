using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知实体——发布态（一份通知一次写入，收件箱行经 <see cref="UserNotificationEntity"/> 关联）。
/// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
/// <para>保留 BCL <c>[Table("Notification")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
/// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
/// </summary>
[Table("Notification")]
[FreeSql.DataAnnotations.Index("IX_Notification_Name", nameof(Name), IsUnique = false)]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class NotificationEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>通知名（稳定唯一，如 "OrderShipped"——与 RegistrationDefinition.Name 对应）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(128)]
    public string Name { get; set; } = "";

    /// <summary>显示名快照（发布时从定义复制，避免运行时查定义）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3, IsNullable = true)]
    [MaxLength(256)]
    public string? DisplayName { get; set; }

    /// <summary>通知数据（键值对 JSON，<see cref="NotificationData.ToJson"/> 产出）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true)]
    [MaxLength(4000)]
    public string? DataJson { get; set; }

    /// <summary>严重级别（<see cref="NotificationSeverity"/> 枚举值）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public int Severity { get; set; }

    /// <summary>关联实体类型（可选，实体级通知——匹配订阅）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6, IsNullable = true)]
    [MaxLength(256)]
    public string? EntityTypeName { get; set; }

    /// <summary>关联实体 ID（可选，实体级通知——匹配订阅）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7, IsNullable = true)]
    [MaxLength(128)]
    public string? EntityId { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8, CanUpdate = false)]
    public DateTime CreateTime { get; set; }
}