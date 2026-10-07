using System;
using FreeSql.DataAnnotations;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批任务-实例视图实体（VEntity）——跨表 JOIN <c>ApprovalTask</c> → <c>ApprovalInstance</c>，
/// 按 InstanceId 单查询返回任务链 + Instance 列（读模型联邦）。
/// <para>V0.3.0：替代 <c>GetInstanceDetailAsync</c> 任务链两步查询（先查任务再查实例列）——JOIN 下推 DB，单查询完成。
/// VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。
/// ⚠️ 敏感视图（含审批人/审批意见列，C4）：ExposeGraphqlQuery = false，数据访问统一经 IApprovalQueryService 门面。</para>
/// </summary>
[Table(Name = "TKWFV_ApprovalTaskView", DisableSyncStructure = true)]
[DomainGenerateCode(IsView = true,
    ViewSql = @"CREATE OR REPLACE VIEW ""TKWFV_ApprovalTaskView"" AS
SELECT t.""Id"", t.""InstanceId"", t.""StepIndex"", t.""StepName"", t.""ApproverType"", t.""ApproverValue"",
       t.""ApproverUserId"", t.""Status"", t.""ApprovedAt"", t.""ApprovedBy"", t.""Comment"", t.""TransferredTo"",
       t.""CreateTime"" AS ""TaskCreateTime"", i.""BusinessType"", i.""BusinessId"", i.""Status"" AS ""InstanceStatus"",
       i.""IsActive"", i.""CurrentStepIndex"", i.""Submitter"", i.""FlowCode"", i.""CreateTime"" AS ""InstanceCreateTime""
FROM ""TKWF_ApprovalTask"" t
INNER JOIN ""TKWF_ApprovalInstance"" i ON t.""InstanceId"" = i.""Id""",
    ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""TKWFV_ApprovalTaskView"" AS
SELECT t.""Id"", t.""InstanceId"", t.""StepIndex"", t.""StepName"", t.""ApproverType"", t.""ApproverValue"",
       t.""ApproverUserId"", t.""Status"", t.""ApprovedAt"", t.""ApprovedBy"", t.""Comment"", t.""TransferredTo"",
       t.""CreateTime"" AS ""TaskCreateTime"", i.""BusinessType"", i.""BusinessId"", i.""Status"" AS ""InstanceStatus"",
       i.""IsActive"", i.""CurrentStepIndex"", i.""Submitter"", i.""FlowCode"", i.""CreateTime"" AS ""InstanceCreateTime""
FROM ""TKWF_ApprovalTask"" t
INNER JOIN ""TKWF_ApprovalInstance"" i ON t.""InstanceId"" = i.""Id""",
    ExposeGraphqlQuery = false, // ⚠️ C4：敏感视图显式关闭（含审批人明细/意见列），默认 true 不关即裸暴露
    DefaultPageSize = 50)]
public partial class ApprovalTaskView
{
    /// <summary>主键——透传 ApprovalTask.Id（每任务唯一稳定）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>关联审批实例 ID（外层过滤键，视图外参数化）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long InstanceId { get; set; }

    /// <summary>步骤序号（从 0 开始，排序主键）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    public int StepIndex { get; set; }

    /// <summary>步骤显示名（快照——如"部门经理""财务"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public string StepName { get; set; } = "";

    /// <summary>审批人类型（User/Role）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public ApprovalApproverType ApproverType { get; set; }

    /// <summary>审批人值（User=用户 ID，Role=角色名）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public string ApproverValue { get; set; } = "";

    /// <summary>审批人用户 ID（解析结果快照 P1-4，待办查询过滤键）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7, IsNullable = true)]
    public string? ApproverUserId { get; set; }

    /// <summary>任务状态（<see cref="ApprovalTaskStatus"/>）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public ApprovalTaskStatus Status { get; set; } = ApprovalTaskStatus.Pending;

    /// <summary>审批通过时间。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
    public DateTime? ApprovedAt { get; set; }

    /// <summary>审批通过人（用户 ID）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10, IsNullable = true)]
    public string? ApprovedBy { get; set; }

    /// <summary>审批意见（可选）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11, IsNullable = true)]
    public string? Comment { get; set; }

    /// <summary>转交给谁（转交后写入——原任务 Transferred 终态）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 12, IsNullable = true)]
    public string? TransferredTo { get; set; }

    /// <summary>任务创建时间（透传 ApprovalTask.CreateTime，排序次键）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 13, CanUpdate = false)]
    public DateTime TaskCreateTime { get; set; }

    /// <summary>业务实体类型名（透传 ApprovalInstance.BusinessType，业务过滤键）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 14)]
    public string BusinessType { get; set; } = "";

    /// <summary>业务实体 ID（透传 ApprovalInstance.BusinessId，业务过滤键）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 15)]
    public string BusinessId { get; set; } = "";

    /// <summary>实例状态（透传 ApprovalInstance.Status，详情展示）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 16)]
    public ApprovalInstanceStatus InstanceStatus { get; set; } = ApprovalInstanceStatus.Draft;

    /// <summary>活动标志（透传 ApprovalInstance.IsActive——true=活动，false=终态）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 17)]
    public bool IsActive { get; set; } = true;

    /// <summary>当前步骤序号（透传 ApprovalInstance.CurrentStepIndex）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 18)]
    public int CurrentStepIndex { get; set; }

    /// <summary>提交人（透传 ApprovalInstance.Submitter，用户 ID 快照）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 19)]
    public string Submitter { get; set; } = "";

    /// <summary>关联审批流编码（透传 ApprovalInstance.FlowCode 快照）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 20)]
    public string FlowCode { get; set; } = "";

    /// <summary>实例创建时间（透传 ApprovalInstance.CreateTime）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 21, CanUpdate = false)]
    public DateTime InstanceCreateTime { get; set; }
}