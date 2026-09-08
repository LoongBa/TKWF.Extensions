using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批任务实体——步骤级待办（审批/驳回/转交载体）。
/// <para>每步骤根据审批人解析结果建一条任务（User 直接建一条 / Role 解析多人建多条）；
/// ApproverUserId 为解析结果快照（P1-4），避免 Role 模式重复查询。</para>
/// </summary>
[Table("ApprovalTask")]
[FreeSql.DataAnnotations.Index("IX_ApprovalTask_Instance", nameof(InstanceId))]
[FreeSql.DataAnnotations.Index("IX_ApprovalTask_Assignee", nameof(ApproverUserId) + "," + nameof(Status))]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class ApprovalTaskEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>关联审批实例 ID。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long InstanceId { get; set; }

    /// <summary>步骤序号（从 0 开始）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    public int StepIndex { get; set; }

    /// <summary>步骤显示名（快照——如"部门经理""财务"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    [MaxLength(128)]
    public string StepName { get; set; } = "";

    /// <summary>审批人类型（User/Role）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public ApprovalApproverType ApproverType { get; set; }

    /// <summary>审批人值（User=用户 ID，Role=角色名）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    [MaxLength(128)]
    public string ApproverValue { get; set; } = "";

    /// <summary>
    /// 审批人用户 ID（解析结果快照 P1-4）——Role 模式下为解析后的实际用户 ID。
    /// <para>C1 安全校验：ApproveAsync/RejectAsync/TransferAsync 校验 approverUserId==task.ApproverUserId。</para>
    /// </summary>
    [FreeSql.DataAnnotations.Column(Position = 7, IsNullable = true)]
    [MaxLength(128)]
    public string? ApproverUserId { get; set; }

    /// <summary>任务状态（<see cref="ApprovalTaskStatus"/>）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public ApprovalTaskStatus Status { get; set; } = ApprovalTaskStatus.Pending;

    /// <summary>审批通过时间。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
    public DateTime? ApprovedAt { get; set; }

    /// <summary>审批通过人（用户 ID）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10, IsNullable = true)]
    [MaxLength(128)]
    public string? ApprovedBy { get; set; }

    /// <summary>审批意见（可选）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11, IsNullable = true)]
    [MaxLength(512)]
    public string? Comment { get; set; }

    /// <summary>转交给谁（转交后写入——原任务 Transferred 终态）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 12, IsNullable = true)]
    [MaxLength(128)]
    public string? TransferredTo { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 13, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
