using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>
/// 加签记录实体——审批运行时当前审批人动态追加审批人（钉钉步骤内模型）。
/// <para>每加签人一行（Participate/Notify 均记录）；Participate 额外创建审批任务，Notify 仅记录 + 事件。
/// 索引 UX_aa_instance_user（InstanceId+UserId，非唯一——同实例可多次加签同一人不同步骤）。</para>
/// </summary>
[Table("ApprovalAppend")]
[FreeSql.DataAnnotations.Index("UX_aa_instance_user", nameof(InstanceId) + "," + nameof(UserId), IsUnique = false)]
[DomainGenerateCode(DefaultPageSize = 50, SubDomain = "Approval", SubDomainRoutePrefix = "/Approval")]
public partial class ApprovalAppendEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>关联审批实例 ID。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long InstanceId { get; set; }

    /// <summary>关联任务 ID（当前步骤触发加签的任务）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    public long TaskId { get; set; }

    /// <summary>步骤序号（加签发生在当前步骤）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public int StepIndex { get; set; }

    /// <summary>加签人用户 ID。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    [MaxLength(128)]
    public string UserId { get; set; } = "";

    /// <summary>操作人（当前审批人——触发加签的用户 ID）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    [MaxLength(128)]
    public string OperatedByUserId { get; set; } = "";

    /// <summary>加签类型（Before/After——v0.2.0 行为一致，固定记录 After）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public ApprovalAppendType AppendType { get; set; } = ApprovalAppendType.After;

    /// <summary>加签模式（Participate 参与审批 / Notify 仅通知）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public ApprovalAppendMode AppendMode { get; set; } = ApprovalAppendMode.Participate;

    /// <summary>加签理由（可选）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
    [MaxLength(512)]
    public string? Remark { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
