using System;
using System.Collections.Generic;

namespace TKWF.Ext.Approval;

// ── 查询输入 ──

/// <summary>审批实例分页查询输入。</summary>
public sealed class ApprovalInstanceQueryInput
{
    /// <summary>业务实体类型（精确匹配）。</summary>
    public string? BusinessType { get; set; }

    /// <summary>业务实体 ID（精确匹配）。</summary>
    public string? BusinessId { get; set; }

    /// <summary>实例状态过滤（null=全部）。</summary>
    public ApprovalInstanceStatus? Status { get; set; }

    /// <summary>提交人过滤。</summary>
    public string? Submitter { get; set; }

    /// <summary>开始时间（大于等于）。</summary>
    public DateTime? FromTime { get; set; }

    /// <summary>结束时间（小于等于）。</summary>
    public DateTime? ToTime { get; set; }

    /// <summary>跳过条数（默认 0）。</summary>
    public int Skip { get; set; }

    /// <summary>返回条数（默认 50，上限 200）。</summary>
    public int Take { get; set; } = 50;
}

/// <summary>审批任务分页查询输入（待办查询）。</summary>
public sealed class ApprovalTaskQueryInput
{
    /// <summary>审批人用户 ID（必填——按当前用户查待办）。</summary>
    public string ApproverUserId { get; set; } = "";

    /// <summary>任务状态过滤（默认 Pending）。</summary>
    public ApprovalTaskStatus? Status { get; set; }

    /// <summary>跳过条数（默认 0）。</summary>
    public int Skip { get; set; }

    /// <summary>返回条数（默认 50，上限 200）。</summary>
    public int Take { get; set; } = 50;
}

// ── 分页结果 ──

/// <summary>审批实例分页结果。</summary>
public sealed class ApprovalInstancePagedResult
{
    /// <summary>总条数。</summary>
    public long Total { get; set; }

    /// <summary>当前页列表。</summary>
    public IReadOnlyList<ApprovalInstanceListItemDto> Items { get; set; } = [];
}

/// <summary>审批任务分页结果。</summary>
public sealed class ApprovalTaskPagedResult
{
    /// <summary>总条数。</summary>
    public long Total { get; set; }

    /// <summary>当前页列表。</summary>
    public IReadOnlyList<ApprovalTaskListItemDto> Items { get; set; } = [];
}

// ── 列表 DTO（不含 BusinessDataJson 大字段）──

/// <summary>审批实例列表 DTO。</summary>
public sealed class ApprovalInstanceListItemDto
{
    public long Id { get; set; }
    public long FlowId { get; set; }
    public string FlowCode { get; set; } = "";
    public string BusinessType { get; set; } = "";
    public string BusinessId { get; set; } = "";
    public ApprovalInstanceStatus Status { get; set; }
    public bool IsActive { get; set; }
    public int CurrentStepIndex { get; set; }
    public string Submitter { get; set; } = "";
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreateTime { get; set; }
}

/// <summary>审批任务列表 DTO。</summary>
public sealed class ApprovalTaskListItemDto
{
    public long Id { get; set; }
    public long InstanceId { get; set; }
    public int StepIndex { get; set; }
    public string StepName { get; set; } = "";
    public ApprovalApproverType ApproverType { get; set; }
    public string ApproverValue { get; set; } = "";
    public string? ApproverUserId { get; set; }
    public ApprovalTaskStatus Status { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Comment { get; set; }
    public string? TransferredTo { get; set; }
    public DateTime CreateTime { get; set; }
}

// ── 详情 DTO（含任务链 + BusinessDataJson）──

/// <summary>
/// 审批实例详情 DTO——含任务链 + BusinessDataJson 全量。
/// <para>列表 DTO 剔除 BusinessDataJson 大字段；详情 GetInstanceDetailAsync 取全量。</para>
/// </summary>
public sealed class ApprovalInstanceDetailDto
{
    public long Id { get; set; }
    public long FlowId { get; set; }
    public string FlowCode { get; set; } = "";
    public string BusinessType { get; set; } = "";
    public string BusinessId { get; set; } = "";
    public ApprovalInstanceStatus Status { get; set; }
    public bool IsActive { get; set; }
    public int CurrentStepIndex { get; set; }
    public string Submitter { get; set; } = "";
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? Reason { get; set; }
    public string? BusinessDataJson { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreateTime { get; set; }

    /// <summary>任务链（该实例的全部审批任务，按 StepIndex+CreateTime 排序）。</summary>
    public IReadOnlyList<ApprovalTaskListItemDto> Tasks { get; set; } = [];
}
