using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批查询服务实现——经 SG1 DataService 委托查询，不注入 IFreeSql/IEntityDAC。
/// <para>Scoped 生命周期。列表 DTO 剔除 BusinessDataJson 大字段（查询性能）；
/// 详情 GetInstanceDetailAsync 取全量 BusinessDataJson + 任务链。</para>
/// <para>ADR88/DI004：DataService 不再构造注入——经 User.Use&lt;T&gt;() 懒加载。</para>
/// <para>V0.3.0（V4.10.53 ADR90 领域自治根治，正确路线）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c>
/// 获取用户上下文（IDomainUser 永不注册 DI）。<c>[DiContractIgnore]</c> 豁免 DI001（AddConstructibleService
/// 运行时手写注册非 SG DI 契约目标）。注册形态改
/// <c>AddConstructibleService&lt;IApprovalQueryService, ApprovalQueryService&gt;</c>（接口可构造守卫工厂 + 实现类 throw-factory）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class ApprovalQueryService(IDomainUser user) : DomainServiceBase(user), IApprovalQueryService
{
    private ApprovalInstanceEntityDataService? _instanceDataService;
    private ApprovalInstanceEntityDataService InstanceDataService => _instanceDataService ??= User.Use<ApprovalInstanceEntityDataService>();
    private ApprovalTaskEntityDataService? _taskDataService;
    private ApprovalTaskEntityDataService TaskDataService => _taskDataService ??= User.Use<ApprovalTaskEntityDataService>();
    private ApprovalTaskViewDataService? _taskViewDataService;
    private ApprovalTaskViewDataService TaskViewDataService => _taskViewDataService ??= User.Use<ApprovalTaskViewDataService>();
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
        var total = await InstanceDataService.CountAsync(predicate, ct);
        var pageItems = await InstanceDataService.EntitySelectAsync(
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
        var total = await TaskDataService.CountAsync(predicate, ct);
        var pageItems = await TaskDataService.EntitySelectAsync(
            predicate, skip, take, q => q.OrderByDescending(t => t.Id), ct);

        var dtos = pageItems.Select(MapToTaskListItemDto).ToList();
        return new ApprovalTaskPagedResult { Total = total, Items = dtos };
    }

    /// <inheritdoc />
    public async Task<ApprovalInstanceDetailDto?> GetInstanceDetailAsync(long instanceId, CancellationToken ct = default)
    {
        var instance = await InstanceDataService.EntityGetAsync(i => i.Id == instanceId, ct);
        if (instance == null) return null;

        // 查询任务链（V0.3.0 VEntity：vw_ApprovalTaskView JOIN 下推 DB，携带 Instance 列，替代任务链两步查询）
        var tasks = await TaskViewDataService.GetTasksByInstanceIdAsync(instanceId, ct);

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

    // V0.3.0 VEntity 视图行映射（视图列名 TaskCreateTime → DTO CreateTime；13 字段 DTO 面保持不变，
    // View→DTO 映射在 QueryService 内——方案 §五）。实体重载保留给 GetPendingTasksAsync（DB 级分页不动）。
    private static ApprovalTaskListItemDto MapToTaskListItemDto(ApprovalTaskView t) => new()
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
        CreateTime = t.TaskCreateTime
    };
}
