using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批查询服务实现——经 SG1 DataService 委托查询，不注入 IFreeSql/IEntityDAC。
/// <para>Scoped 生命周期。列表 DTO 剔除 BusinessDataJson 大字段（查询性能）；
/// 详情 GetInstanceDetailAsync 取全量 BusinessDataJson + 任务链。</para>
/// </summary>
internal sealed class ApprovalQueryService(
    ApprovalInstanceEntityDataService instanceDataService,
    ApprovalTaskEntityDataService taskDataService) : IApprovalQueryService
{
    /// <inheritdoc />
    public async Task<ApprovalInstancePagedResult> GetInstancesAsync(ApprovalInstanceQueryInput input, CancellationToken ct = default)
    {
        // 规范化分页
        var skip = Math.Max(0, input.Skip);
        var take = Math.Clamp(input.Take <= 0 ? 50 : input.Take, 1, 200);

        // 构建查询条件
        Expression<Func<ApprovalInstanceEntity, bool>> predicate = i =>
            (input.BusinessType == null || i.BusinessType == input.BusinessType)
            && (input.BusinessId == null || i.BusinessId == input.BusinessId)
            && (input.Status == null || i.Status == input.Status)
            && (input.Submitter == null || i.Submitter == input.Submitter)
            && (input.FromTime == null || i.CreateTime >= input.FromTime.Value)
            && (input.ToTime == null || i.CreateTime <= input.ToTime.Value);

        // P2-1/P5：DB 级分页——total 独立 count + 页数据 skip/take 下推（不再 100k 全量内存分页）
        var total = await instanceDataService.CountAsync(predicate, ct);
        var pageItems = await instanceDataService.EntitySelectAsync(
            predicate, skip, take, q => q.OrderByDescending(i => i.Id), ct);

        var dtos = pageItems.Select(MapToListItemDto).ToList();
        return new ApprovalInstancePagedResult { Total = total, Items = dtos };
    }

    /// <inheritdoc />
    public async Task<ApprovalTaskPagedResult> GetPendingTasksAsync(ApprovalTaskQueryInput input, CancellationToken ct = default)
    {
        var skip = Math.Max(0, input.Skip);
        var take = Math.Clamp(input.Take <= 0 ? 50 : input.Take, 1, 200);
        var status = input.Status ?? ApprovalTaskStatus.Pending;

        Expression<Func<ApprovalTaskEntity, bool>> predicate =
            t => t.ApproverUserId == input.ApproverUserId && t.Status == status;

        // P2-1/P5：DB 级分页——total 独立 count + 页数据 skip/take 下推
        var total = await taskDataService.CountAsync(predicate, ct);
        var pageItems = await taskDataService.EntitySelectAsync(
            predicate, skip, take, q => q.OrderByDescending(t => t.Id), ct);

        var dtos = pageItems.Select(MapToTaskListItemDto).ToList();
        return new ApprovalTaskPagedResult { Total = total, Items = dtos };
    }

    /// <inheritdoc />
    public async Task<ApprovalInstanceDetailDto?> GetInstanceDetailAsync(long instanceId, CancellationToken ct = default)
    {
        var instance = await instanceDataService.EntityGetAsync(i => i.Id == instanceId, ct);
        if (instance == null) return null;

        // 查询任务链
        var tasks = await taskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId,
            0, 10000, q => q.OrderBy(t => t.StepIndex).ThenBy(t => t.Id), ct);

        return new ApprovalInstanceDetailDto
        {
            Id = instance.Id,
            FlowId = instance.FlowId,
            FlowCode = instance.FlowCode,
            BusinessType = instance.BusinessType,
            BusinessId = instance.BusinessId,
            Status = instance.Status,
            IsActive = instance.IsActive,
            CurrentStepIndex = instance.CurrentStepIndex,
            Submitter = instance.Submitter,
            SubmittedAt = instance.SubmittedAt,
            ApprovedAt = instance.ApprovedAt,
            RejectedAt = instance.RejectedAt,
            Reason = instance.Reason,
            BusinessDataJson = instance.BusinessDataJson, // 全量
            CorrelationId = instance.CorrelationId,
            CreateTime = instance.CreateTime,
            Tasks = tasks.Select(MapToTaskListItemDto).ToList()
        };
    }

    // ── 映射 ──

    private static ApprovalInstanceListItemDto MapToListItemDto(ApprovalInstanceEntity i) => new()
    {
        Id = i.Id,
        FlowId = i.FlowId,
        FlowCode = i.FlowCode,
        BusinessType = i.BusinessType,
        BusinessId = i.BusinessId,
        Status = i.Status,
        IsActive = i.IsActive,
        CurrentStepIndex = i.CurrentStepIndex,
        Submitter = i.Submitter,
        SubmittedAt = i.SubmittedAt,
        ApprovedAt = i.ApprovedAt,
        RejectedAt = i.RejectedAt,
        Reason = i.Reason,
        CorrelationId = i.CorrelationId,
        CreateTime = i.CreateTime
    };

    private static ApprovalTaskListItemDto MapToTaskListItemDto(ApprovalTaskEntity t) => new()
    {
        Id = t.Id,
        InstanceId = t.InstanceId,
        StepIndex = t.StepIndex,
        StepName = t.StepName,
        ApproverType = t.ApproverType,
        ApproverValue = t.ApproverValue,
        ApproverUserId = t.ApproverUserId,
        Status = t.Status,
        ApprovedAt = t.ApprovedAt,
        ApprovedBy = t.ApprovedBy,
        Comment = t.Comment,
        TransferredTo = t.TransferredTo,
        CreateTime = t.CreateTime
    };
}
