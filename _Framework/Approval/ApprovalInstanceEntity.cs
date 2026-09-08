using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批实例实体——审批流程的业务实例（业务实体弱关联 + 状态机 + 防重复唯一约束）。
/// <para>唯一约束 <c>UX_ApprovalInstance_Active</c>（BusinessType+BusinessId+IsActive）——活动实例（IsActive=true）同业务唯一防重复；
/// 终态 IsActive=false 释放约束，允许重新提交新实例（评审 C2）。</para>
/// </summary>
[Table("ApprovalInstance")]
[FreeSql.DataAnnotations.Index("UX_ApprovalInstance_Active",
    nameof(BusinessType) + "," + nameof(BusinessId) + "," + nameof(IsActive), IsUnique = true)]
[FreeSql.DataAnnotations.Index("IX_ApprovalInstance_Status_Business", nameof(Status) + "," + nameof(BusinessType) + "," + nameof(BusinessId))]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class ApprovalInstanceEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>关联审批流定义 ID。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long FlowId { get; set; }

    /// <summary>关联审批流编码（快照——冗余查询便利）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(64)]
    public string FlowCode { get; set; } = "";

    /// <summary>业务实体类型名（如 "Expense"、"LeaveRequest"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    [MaxLength(128)]
    public string BusinessType { get; set; } = "";

    /// <summary>业务实体 ID（如 "exp-001"、"lr-002"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    [MaxLength(128)]
    public string BusinessId { get; set; } = "";

    /// <summary>实例状态（<see cref="ApprovalInstanceStatus"/>）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public ApprovalInstanceStatus Status { get; set; } = ApprovalInstanceStatus.Draft;

    /// <summary>
    /// 活动标志（C2 核心）：true=活动实例（Draft/Pending），false=终态（Approved/Rejected/Withdrawn）。
    /// <para>UX_ApprovalInstance_Active 唯一约束保证同业务仅一个活动实例；终态释放约束允许重新提交。</para>
    /// </summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public bool IsActive { get; set; } = true;

    /// <summary>当前步骤序号（从 0 开始）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public int CurrentStepIndex { get; set; }

    /// <summary>提交人（用户 ID 快照）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9)]
    [MaxLength(128)]
    public string Submitter { get; set; } = "";

    /// <summary>提交时间（SubmitAsync 写入）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10, IsNullable = true)]
    public DateTime? SubmittedAt { get; set; }

    /// <summary>通过时间（终态 Approved 写入）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11, IsNullable = true)]
    public DateTime? ApprovedAt { get; set; }

    /// <summary>驳回时间（终态 Rejected 写入）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 12, IsNullable = true)]
    public DateTime? RejectedAt { get; set; }

    /// <summary>驳回/撤回原因（可选）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 13, IsNullable = true)]
    [MaxLength(1024)]
    public string? Reason { get; set; }

    /// <summary>业务数据快照 JSON（可选——轻量快照，不跨模块引用实体）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 14, IsNullable = true, StringLength = -1)]
    public string? BusinessDataJson { get; set; }

    /// <summary>关联 ID（可选——用于跨系统关联追踪）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 15, IsNullable = true)]
    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 16, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
