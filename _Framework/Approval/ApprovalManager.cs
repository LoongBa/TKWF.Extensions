using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Events;
using TKW.Framework.Domain.Transactions;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批引擎核心实现——流程定义管理 + 实例生命周期 + 任务审批/驳回/转交/撤回 + 事件派发。
/// <para>Scoped 生命周期。注入 SG1 DataService 委托 + ITransactionManager + ILocalEventBus + IApprovalAssigneeResolver。
/// 状态机显式迁移（PrintTemplates 范式）：每动作方法内校验合法迁移，非法抛 <see cref="InvalidApprovalOperationException"/>。
/// 事务原子提交（C3）：ITransactionManager.BeginAsync → 业务 → CommitAsync → ILocalEventBus.PublishAsync。</para>
/// </summary>
internal sealed class ApprovalManager(
    ApprovalFlowEntityDataService flowDataService,
    ApprovalInstanceEntityDataService instanceDataService,
    ApprovalTaskEntityDataService taskDataService,
    ITransactionManager transactionManager,
    ILocalEventBus localEventBus,
    IApprovalAssigneeResolver assigneeResolver,
    ILogger<ApprovalManager> logger) : IApprovalService
{
    // ── 流程定义 ──

    /// <inheritdoc />
    public async Task<long> CreateFlowAsync(string code, string name, IReadOnlyList<ApprovalStepDefinition> steps,
        string? description = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (steps == null || steps.Count == 0)
            throw new ArgumentException("审批步骤不能为空", nameof(steps));

        var entity = new ApprovalFlowEntity
        {
            Code = code,
            Name = name,
            Description = description,
            StepsJson = ApprovalStepDefinitionSerializer.Serialize(steps),
            IsEnabled = true,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow
        };

        // 唯一索引 UX_ApprovalFlow_Code 并发冲突由数据库异常自然传播（败者显式异常）
        await flowDataService.EntityCreateAsync(entity, ct);
        return entity.Id;
    }

    /// <inheritdoc />
    public async Task UpdateFlowAsync(long flowId, string name, IReadOnlyList<ApprovalStepDefinition> steps,
        string? description = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (steps == null || steps.Count == 0)
            throw new ArgumentException("审批步骤不能为空", nameof(steps));

        var entity = await flowDataService.EntityGetAsync(f => f.Id == flowId, ct)
            ?? throw new InvalidApprovalOperationException($"审批流 {flowId} 不存在");

        entity.Name = name;
        entity.Description = description;
        entity.StepsJson = ApprovalStepDefinitionSerializer.Serialize(steps);
        entity.UpdateTime = DateTime.UtcNow;

        await flowDataService.EntityUpdateAsync(entity, ct);
    }

    /// <inheritdoc />
    public async Task EnableFlowAsync(long flowId, CancellationToken ct = default)
    {
        var entity = await flowDataService.EntityGetAsync(f => f.Id == flowId, ct)
            ?? throw new InvalidApprovalOperationException($"审批流 {flowId} 不存在");
        entity.IsEnabled = true;
        entity.UpdateTime = DateTime.UtcNow;
        await flowDataService.EntityUpdateAsync(entity, ct);
    }

    /// <inheritdoc />
    public async Task DisableFlowAsync(long flowId, CancellationToken ct = default)
    {
        var entity = await flowDataService.EntityGetAsync(f => f.Id == flowId, ct)
            ?? throw new InvalidApprovalOperationException($"审批流 {flowId} 不存在");
        entity.IsEnabled = false;
        entity.UpdateTime = DateTime.UtcNow;
        await flowDataService.EntityUpdateAsync(entity, ct);
    }

    // ── 实例 ──

    /// <inheritdoc />
    public async Task<long> StartAsync(string businessType, string businessId, string flowCode, string submitter,
        string? businessDataJson = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessType);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(flowCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(submitter);

        // P1-3：校验流程启用
        var flow = await flowDataService.EntityGetAsync(f => f.Code == flowCode, ct)
            ?? throw new InvalidApprovalOperationException($"审批流 '{flowCode}' 不存在");
        if (!flow.IsEnabled)
            throw new InvalidApprovalOperationException($"审批流 '{flowCode}' 已禁用，无法启动");

        // C2：前置检查同业务是否有活动实例
        var existingActive = await instanceDataService.EntityGetAsync(
            i => i.BusinessType == businessType && i.BusinessId == businessId && i.IsActive, ct);
        if (existingActive != null)
            throw new InvalidApprovalOperationException(
                $"该业务已有活动审批实例（InstanceId={existingActive.Id}），不可重复启动");

        var instance = new ApprovalInstanceEntity
        {
            FlowId = flow.Id,
            FlowCode = flowCode,
            BusinessType = businessType,
            BusinessId = businessId,
            Status = ApprovalInstanceStatus.Draft,
            IsActive = true,
            CurrentStepIndex = 0,
            Submitter = submitter,
            BusinessDataJson = businessDataJson,
            CreateTime = DateTime.UtcNow
        };

        await instanceDataService.EntityCreateAsync(instance, ct);
        return instance.Id;
    }

    /// <inheritdoc />
    public async Task SubmitAsync(long instanceId, CancellationToken ct = default)
    {
        var instance = await instanceDataService.EntityGetAsync(i => i.Id == instanceId, ct)
            ?? throw new InvalidApprovalOperationException($"审批实例 {instanceId} 不存在");

        // 状态机：Draft→Pending
        ValidateTransition(instance.Status, ApprovalInstanceStatus.Draft, ApprovalInstanceStatus.Pending);

        // 解析首步（事务外读取流程定义——只读数据）
        var steps = ApprovalStepDefinitionSerializer.Deserialize(
            (await flowDataService.EntityGetAsync(f => f.Id == instance.FlowId, ct))?.StepsJson ?? "[]");
        if (steps.Count == 0)
            throw new InvalidApprovalOperationException("审批流无步骤定义");

        // 事务包裹：状态变更 + 任务创建原子提交
        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            instance.Status = ApprovalInstanceStatus.Pending;
            instance.SubmittedAt = DateTime.UtcNow;
            await instanceDataService.EntityUpdateAsync(instance, ct);

            await CreateTasksForStepAsync(instance, steps[0], ct);

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }
    }

    // ── 任务 ──

    /// <inheritdoc />
    public async Task ApproveAsync(long taskId, string approverUserId, string? comment = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approverUserId);

        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

        // C1：审批人身份硬校验
        ValidateApproverIdentity(task, approverUserId);

        // 状态机：Pending→Approved
        ValidateTaskTransition(task.Status, ApprovalTaskStatus.Approved);

        // 事务包裹：任务状态 + 步骤判定 + 实例推进原子提交
        ApprovalInstanceStatus? terminalStatus = null;
        ApprovalInstanceEntity? terminalInstance = null;

        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            task.Status = ApprovalTaskStatus.Approved;
            task.ApprovedAt = DateTime.UtcNow;
            task.ApprovedBy = approverUserId;
            task.Comment = comment;
            await taskDataService.EntityUpdateAsync(task, ct);

            // 获取实例
            var instance = await instanceDataService.EntityGetAsync(i => i.Id == task.InstanceId, ct)
                ?? throw new InvalidApprovalOperationException($"审批实例 {task.InstanceId} 不存在");

            // 步骤判定
            var steps = ApprovalStepDefinitionSerializer.Deserialize(
                (await flowDataService.EntityGetAsync(f => f.Id == instance.FlowId, ct))?.StepsJson ?? "[]");
            var currentStep = steps[task.StepIndex];

            // 查同步骤全部任务
            var allTasksForStep = await taskDataService.EntitySelectAsync(
                t => t.InstanceId == instance.Id && t.StepIndex == task.StepIndex,
                0, 1000, q => q.OrderBy(t => t.Id), ct);

            bool stepCompleted = currentStep.Mode switch
            {
                ApprovalMode.Any => true, // 或签：任一通过即步骤通过
                ApprovalMode.All => allTasksForStep.All(t => t.Status == ApprovalTaskStatus.Approved),
                _ => false
            };

            if (stepCompleted)
            {
                // 将其余 Pending 任务标记 Completed（Any：任一通过即完成；All：全部通过后清理残留）
                foreach (var otherTask in allTasksForStep.Where(t =>
                    t.Id != taskId && t.Status == ApprovalTaskStatus.Pending))
                {
                    otherTask.Status = ApprovalTaskStatus.Completed;
                    await taskDataService.EntityUpdateAsync(otherTask, ct);
                }

                // 下一步？
                int nextStepIndex = task.StepIndex + 1;
                if (nextStepIndex < steps.Count)
                {
                    // 还有下一步——更新 CurrentStepIndex + 创建下一步任务
                    instance.CurrentStepIndex = nextStepIndex;
                    await instanceDataService.EntityUpdateAsync(instance, ct);
                    await CreateTasksForStepAsync(instance, steps[nextStepIndex], ct);
                }
                else
                {
                    // 全步骤通过→实例 Approved（终态 + IsActive=false 释放约束）
                    ApplyTerminalStatus(instance, ApprovalInstanceStatus.Approved, null);
                    await instanceDataService.EntityUpdateAsync(instance, ct);
                    terminalStatus = ApprovalInstanceStatus.Approved;
                    terminalInstance = instance;
                }
            }

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }

        // C3：CommitAsync 成功后 ILocalEventBus.PublishAsync（事务外派发）
        if (terminalStatus.HasValue && terminalInstance is not null)
        {
            await RaiseCompletionEventAsync(terminalInstance, terminalStatus.Value, null, ct);
        }
    }

    /// <inheritdoc />
    public async Task RejectAsync(long taskId, string approverUserId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approverUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

        // C1：审批人身份硬校验
        ValidateApproverIdentity(task, approverUserId);

        // 状态机：Pending→Rejected
        ValidateTaskTransition(task.Status, ApprovalTaskStatus.Rejected);

        // 事务包裹：任务状态 + 同步骤清理 + 实例终态原子提交
        ApprovalInstanceEntity? terminalInstance = null;

        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            task.Status = ApprovalTaskStatus.Rejected;
            task.ApprovedAt = DateTime.UtcNow;
            task.ApprovedBy = approverUserId;
            task.Comment = reason;
            await taskDataService.EntityUpdateAsync(task, ct);

            // 获取实例
            var instance = await instanceDataService.EntityGetAsync(i => i.Id == task.InstanceId, ct)
                ?? throw new InvalidApprovalOperationException($"审批实例 {task.InstanceId} 不存在");

            // 将同步骤其余 Pending 任务标记 Completed
            var allTasksForStep = await taskDataService.EntitySelectAsync(
                t => t.InstanceId == instance.Id && t.StepIndex == task.StepIndex,
                0, 1000, q => q.OrderBy(t => t.Id), ct);
            foreach (var otherTask in allTasksForStep.Where(t =>
                t.Id != taskId && t.Status == ApprovalTaskStatus.Pending))
            {
                otherTask.Status = ApprovalTaskStatus.Completed;
                await taskDataService.EntityUpdateAsync(otherTask, ct);
            }

            // 实例 Rejected（终态 + IsActive=false 释放约束）
            ApplyTerminalStatus(instance, ApprovalInstanceStatus.Rejected, reason);
            await instanceDataService.EntityUpdateAsync(instance, ct);
            terminalInstance = instance;

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }

        // C3：CommitAsync 成功后 ILocalEventBus.PublishAsync（事务外派发）
        if (terminalInstance is not null)
        {
            await RaiseCompletionEventAsync(terminalInstance, ApprovalInstanceStatus.Rejected, reason, ct);
        }
    }

    /// <inheritdoc />
    public async Task TransferAsync(long taskId, string fromUserId, string toUserId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toUserId);

        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

        // C1+C4：仅任务审批人可转交
        ValidateApproverIdentity(task, fromUserId);

        // 状态机：Pending→Transferred
        ValidateTaskTransition(task.Status, ApprovalTaskStatus.Transferred);

        // 事务包裹：旧任务状态 + 新任务创建原子提交
        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            task.Status = ApprovalTaskStatus.Transferred;
            task.TransferredTo = toUserId;
            await taskDataService.EntityUpdateAsync(task, ct);

            // 获取实例（获取当前步骤名）
            var instance = await instanceDataService.EntityGetAsync(i => i.Id == task.InstanceId, ct)
                ?? throw new InvalidApprovalOperationException($"审批实例 {task.InstanceId} 不存在");
            var steps = ApprovalStepDefinitionSerializer.Deserialize(
                (await flowDataService.EntityGetAsync(f => f.Id == instance.FlowId, ct))?.StepsJson ?? "[]");
            var currentStep = steps[task.StepIndex];

            // 创建新任务给 toUserId（ApproverType=User，ApproverValue=toUserId，ApproverUserId=toUserId）
            var newTask = new ApprovalTaskEntity
            {
                InstanceId = task.InstanceId,
                StepIndex = task.StepIndex,
                StepName = currentStep.Name,
                ApproverType = ApprovalApproverType.User,
                ApproverValue = toUserId,
                ApproverUserId = toUserId,
                Status = ApprovalTaskStatus.Pending,
                CreateTime = DateTime.UtcNow
            };
            await taskDataService.EntityCreateAsync(newTask, ct);

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task WithdrawAsync(long instanceId, string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var instance = await instanceDataService.EntityGetAsync(i => i.Id == instanceId, ct)
            ?? throw new InvalidApprovalOperationException($"审批实例 {instanceId} 不存在");

        // P1-1：仅提交人可撤回
        if (instance.Submitter != userId)
            throw new InvalidApprovalOperationException("仅提交人可撤回审批");

        // 状态机：Draft→Withdrawn 或 Pending→Withdrawn
        if (instance.Status == ApprovalInstanceStatus.Draft)
        {
            instance.Status = ApprovalInstanceStatus.Withdrawn;
        }
        else if (instance.Status == ApprovalInstanceStatus.Pending)
        {
            instance.Status = ApprovalInstanceStatus.Withdrawn;
            // 将所有 Pending 任务标记 Completed
            var tasks = await taskDataService.EntitySelectAsync(
                t => t.InstanceId == instanceId && t.Status == ApprovalTaskStatus.Pending,
                0, 1000, q => q, ct);
            foreach (var task in tasks)
            {
                task.Status = ApprovalTaskStatus.Completed;
                await taskDataService.EntityUpdateAsync(task, ct);
            }
        }
        else
        {
            throw new InvalidApprovalOperationException(
                $"审批实例当前状态为 {instance.Status}，不可撤回（仅 Draft/Pending 可撤回）");
        }

        // 终态：IsActive=false 释放约束
        instance.IsActive = false;
        await instanceDataService.EntityUpdateAsync(instance, ct);
    }

    // ── 内部方法 ──

    /// <summary>
    /// 完成实例（Approved/Rejected）+ IsActive=false 释放约束。
    /// <para>调用方负责事务包裹（BeginAsync/CommitAsync）和事件派发（C3：CommitAsync 后 PublishAsync）。</para>
    /// </summary>
    private async Task CompleteInstanceAsync(ApprovalInstanceEntity instance,
        ApprovalInstanceStatus terminalStatus, string? reason, CancellationToken ct)
    {
        ApplyTerminalStatus(instance, terminalStatus, reason);
        await instanceDataService.EntityUpdateAsync(instance, ct);
    }

    /// <summary>应用终态字段到实例（纯内存操作，不涉及 DB）。</summary>
    private static void ApplyTerminalStatus(ApprovalInstanceEntity instance,
        ApprovalInstanceStatus terminalStatus, string? reason)
    {
        var now = DateTime.UtcNow;
        instance.Status = terminalStatus;
        instance.IsActive = false; // C2：终态释放唯一约束
        instance.Reason = reason;

        if (terminalStatus == ApprovalInstanceStatus.Approved)
            instance.ApprovedAt = now;
        else if (terminalStatus == ApprovalInstanceStatus.Rejected)
            instance.RejectedAt = now;
    }

    /// <summary>
    /// 派发审批完成事件（C3：事务提交后调用，ILocalEventBus 事务外派发）。
    /// 仅 Approved/Rejected 终态派发；Withdrawn 不派发（撤回属于用户主动取消，非审批结果）。
    /// </summary>
    private async Task RaiseCompletionEventAsync(ApprovalInstanceEntity instance,
        ApprovalInstanceStatus terminalStatus, string? reason, CancellationToken ct)
    {
        if (terminalStatus is not (ApprovalInstanceStatus.Approved or ApprovalInstanceStatus.Rejected))
            return;

        var evt = new ApprovalCompletedEvent(
            instance.Id, instance.BusinessType, instance.BusinessId, terminalStatus, reason);
        try
        {
            await localEventBus.PublishAsync(evt);
            logger.LogInformation("审批完成事件已派发: InstanceId={InstanceId}, Result={Result}",
                instance.Id, terminalStatus);
        }
        catch (Exception ex)
        {
            // 事件 handler 异常不回滚审批（D15 语义）——记录日志
            logger.LogWarning(ex, "审批完成事件派发失败（不影响审批结果）: InstanceId={InstanceId}", instance.Id);
        }
    }

    /// <summary>
    /// 为指定步骤创建审批任务（解析审批人 → 建任务，ApproverUserId = 解析结果快照 P1-4）。
    /// </summary>
    private async Task CreateTasksForStepAsync(ApprovalInstanceEntity instance,
        ApprovalStepDefinition step, CancellationToken ct)
    {
        var assigneeIds = await assigneeResolver.ResolveUserIdsAsync(step, ct);
        if (assigneeIds.Count == 0)
        {
            logger.LogWarning("审批步骤 {StepIndex}({StepName}) 无审批人，跳过", step.Index, step.Name);
            return;
        }

        foreach (var userId in assigneeIds)
        {
            var task = new ApprovalTaskEntity
            {
                InstanceId = instance.Id,
                StepIndex = step.Index,
                StepName = step.Name,
                ApproverType = step.ApproverType,
                ApproverValue = step.ApproverValue,
                ApproverUserId = userId, // P1-4：解析结果快照
                Status = ApprovalTaskStatus.Pending,
                CreateTime = DateTime.UtcNow
            };
            await taskDataService.EntityCreateAsync(task, ct);
        }
    }

    /// <summary>校验实例状态迁移合法性。</summary>
    private static void ValidateTransition(ApprovalInstanceStatus current,
        ApprovalInstanceStatus from, ApprovalInstanceStatus to)
    {
        if (current != from)
            throw new InvalidApprovalOperationException(
                $"审批实例状态为 {current}，不允许从 {from} 迁移到 {to}");
    }

    /// <summary>校验任务状态迁移合法性。</summary>
    private static void ValidateTaskTransition(ApprovalTaskStatus current, ApprovalTaskStatus to)
    {
        if (current != ApprovalTaskStatus.Pending)
            throw new InvalidApprovalOperationException(
                $"审批任务状态为 {current}，不可执行操作（仅 Pending 状态可操作）");
    }

    /// <summary>C1：审批人身份硬校验——approverUserId 必须等于 task.ApproverUserId。</summary>
    private static void ValidateApproverIdentity(ApprovalTaskEntity task, string approverUserId)
    {
        if (string.IsNullOrEmpty(task.ApproverUserId))
            throw new InvalidApprovalOperationException($"任务 {task.Id} 无审批人（ApproverUserId 为空）");
        if (task.ApproverUserId != approverUserId)
            throw new InvalidApprovalOperationException(
                $"非任务审批人无权操作（当前审批人: {task.ApproverUserId}，请求人: {approverUserId}）");
    }
}
