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
/// 审批引擎核心实现——流程定义管理 + 实例生命周期 + 任务审批/驳回/转交/撤回 + 委派/加签/抄送/超时系统动作 + 事件派发。
/// <para>Scoped 生命周期。注入 SG1 DataService 委托 + ITransactionManager + ILocalEventBus + IApprovalAssigneeResolver。
/// 状态机显式迁移（PrintTemplates 范式）：每动作方法内校验合法迁移，非法抛 <see cref="InvalidApprovalOperationException"/>。
/// 事务原子提交（C3）：ITransactionManager.BeginAsync → 业务 → CommitAsync → ILocalEventBus.PublishAsync。</para>
/// <para>v0.2.0：委派（两阶段）+ 加签（步骤内并行）+ 抄送（独立实体 + 事件）+ 超时系统动作（internal——同程序集
/// ApprovalTimeoutService 调用，跳过 C1 身份校验但保留状态机校验，审计 "system:timeout" 标识，C1/P4）。</para>
/// </summary>
internal sealed class ApprovalManager(
    ApprovalFlowEntityDataService flowDataService,
    ApprovalInstanceEntityDataService instanceDataService,
    ApprovalTaskEntityDataService taskDataService,
    ApprovalAppendEntityDataService appendDataService,
    ApprovalCCEntityDataService ccDataService,
    ITransactionManager transactionManager,
    ILocalEventBus localEventBus,
    IApprovalAssigneeResolver assigneeResolver,
    ILogger<ApprovalManager> logger) : IApprovalService
{
    /// <summary>系统超时动作审计标识（D15——不冒充审批人）。</summary>
    internal const string SystemTimeoutActor = "system:timeout";

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
        string? businessDataJson = null, CancellationToken ct = default, string[]? ccUserIds = null)
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

        // v0.2.0：抄送（Position=Start）——StartAsync 带 ccUserIds 记录；通知事件于 SubmitAsync 提交后派发
        if (ccUserIds is { Length: > 0 })
        {
            var now = DateTime.UtcNow;
            foreach (var userId in ccUserIds.Distinct(StringComparer.Ordinal))
            {
                await ccDataService.EntityCreateAsync(new ApprovalCCEntity
                {
                    InstanceId = instance.Id,
                    UserId = userId,
                    Position = ApprovalCCPosition.Start,
                    CreateTime = now
                }, ct);
            }
        }

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

        // v0.2.0：提交后派发 Start 位置抄送事件（post-commit，C3 范式；Result=null——尚未有审批结果）
        await RaiseCcEventAsync(instance, null, ApprovalCCPosition.Start, ct);
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

        // v0.2.0 委派语义：委派中（Pending）委派人不可 Complete——须先 Resolve（Flowable: "A delegated task
        // cannot be completed, but should be resolved instead"）。仅保护用户主动操作（系统超时动作经
        // ApproveAsSystemAsync 绕过此检查，C1/P4——委派中任务超时仍可自动处理）。
        if (task.DelegationState == ApprovalDelegationState.Pending)
            throw new InvalidApprovalOperationException(
                "委派中的任务不可直接审批通过，须先由委派人 ResolveTaskAsync 提交结果");

        await ApproveCoreAsync(taskId, approverUserId, comment, ct);
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

        await RejectCoreAsync(taskId, approverUserId, reason, ct);
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

        await TransferCoreAsync(taskId, toUserId, ct);
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

        // P2-7（P9 评审）：补齐事务包裹——实例状态 + 任务 Completed 原子提交（v0.1.0 遗留）
        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
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
                    0, int.MaxValue, q => q, ct);
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

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }
    }

    // ── v0.2.0：委派（Flowable 两阶段模型）──

    /// <inheritdoc />
    public async Task DelegateTaskAsync(long taskId, string fromUserId, string toUserId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toUserId);

        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

        // C1：仅当前审批人可委派
        ValidateApproverIdentity(task, fromUserId);

        // 状态机：Pending 才可委派
        ValidateTaskTransition(task.Status, ApprovalTaskStatus.Pending);

        // P1：委派单层不嵌套——Pending 拒绝再委派；Resolve 后（Resolved）不回退 None，原审批人不可再次委派（须 Transfer）
        if (task.DelegationState != ApprovalDelegationState.None)
            throw new InvalidApprovalOperationException(
                $"任务当前委派状态为 {task.DelegationState}，不可再委派（委派单层不嵌套——Resolve 后须用 Transfer 替代）");

        if (string.Equals(fromUserId, toUserId, StringComparison.Ordinal))
            throw new InvalidApprovalOperationException("不能将任务委派给自己");

        // 事务包裹：同任务状态切换（ApproverUserId 切换 + 委派标记）——不创建新任务
        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            task.DelegationState = ApprovalDelegationState.Pending;
            task.OriginalAssigneeId = fromUserId;
            task.DelegatedToUserId = toUserId;
            task.ApproverUserId = toUserId; // 委派人成为当前处理人
            task.DelegatedAt = DateTime.UtcNow;
            await taskDataService.EntityUpdateAsync(task, ct);

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ResolveTaskAsync(long taskId, string delegateUserId, string? comment = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(delegateUserId);

        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

        // C1：仅当前处理人（委派人）可 Resolve
        ValidateApproverIdentity(task, delegateUserId);

        // 状态机：Pending 才可 Resolve
        ValidateTaskTransition(task.Status, ApprovalTaskStatus.Pending);

        // 仅委派中（Pending）可 Resolve
        if (task.DelegationState != ApprovalDelegationState.Pending)
            throw new InvalidApprovalOperationException(
                $"任务当前委派状态为 {task.DelegationState}，不可 Resolve（仅委派中可 Resolve）");

        // 事务包裹：Resolved + ApproverUserId 回原审批人
        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            task.DelegationState = ApprovalDelegationState.Resolved;
            task.ApproverUserId = task.OriginalAssigneeId; // 回到原审批人
            task.Comment = comment;
            await taskDataService.EntityUpdateAsync(task, ct);

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }
    }

    // ── v0.2.0：加签（钉钉步骤内模型）──

    /// <inheritdoc />
    public async Task AppendApproverAsync(long taskId, string operatedByUserId, string[] appenderUserIds,
        ApprovalAppendMode mode = ApprovalAppendMode.Participate,
        string? remark = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatedByUserId);
        ArgumentNullException.ThrowIfNull(appenderUserIds);

        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

        // C1：仅当前审批人可加签
        ValidateApproverIdentity(task, operatedByUserId);

        // 状态机：Pending 才可加签
        ValidateTaskTransition(task.Status, ApprovalTaskStatus.Pending);

        // C2 去重：自身 Distinct + 与同步骤 Pending 任务审批人交集拒绝
        var distinctAppenders = appenderUserIds.Distinct(StringComparer.Ordinal).ToList();
        if (distinctAppenders.Count == 0)
            throw new ArgumentException("加签用户列表不能为空", nameof(appenderUserIds));

        var pendingApproverIds = (await taskDataService.EntitySelectAsync(
            t => t.InstanceId == task.InstanceId && t.StepIndex == task.StepIndex
                 && t.Status == ApprovalTaskStatus.Pending,
            0, int.MaxValue, q => q, ct))
            .Select(t => t.ApproverUserId)
            .Where(u => !string.IsNullOrEmpty(u))
            .ToHashSet(StringComparer.Ordinal);

        var duplicate = distinctAppenders.FirstOrDefault(u => pendingApproverIds.Contains(u));
        if (duplicate != null)
            throw new InvalidApprovalOperationException($"用户 {duplicate} 已是本步骤审批人，不可重复加签");

        // 获取当前步骤定义（StepName 快照 + 超时配置派生 P4）
        var instance = await instanceDataService.EntityGetAsync(i => i.Id == task.InstanceId, ct)
            ?? throw new InvalidApprovalOperationException($"审批实例 {task.InstanceId} 不存在");
        var steps = ApprovalStepDefinitionSerializer.Deserialize(
            (await flowDataService.EntityGetAsync(f => f.Id == instance.FlowId, ct))?.StepsJson ?? "[]");
        var currentStep = steps[task.StepIndex];
        var now = DateTime.UtcNow;

        // 事务包裹：记录加签 + 创建加签任务原子提交
        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            foreach (var userId in distinctAppenders)
            {
                // 每加签人一行记录（Participate/Notify 均记录）
                await appendDataService.EntityCreateAsync(new ApprovalAppendEntity
                {
                    InstanceId = task.InstanceId,
                    TaskId = task.Id,
                    StepIndex = task.StepIndex,
                    UserId = userId,
                    OperatedByUserId = operatedByUserId,
                    AppendType = ApprovalAppendType.After, // P8：v0.2.0 Before/After 行为一致，固定记录 After
                    AppendMode = mode,
                    Remark = remark,
                    CreateTime = now
                }, ct);

                if (mode == ApprovalAppendMode.Participate)
                {
                    // 创建加签任务（StepName 含加签标记 + 超时继承步骤配置 P4）——加签人走既有 ApproveAsync（C1 校验）
                    var newTask = new ApprovalTaskEntity
                    {
                        InstanceId = task.InstanceId,
                        StepIndex = task.StepIndex,
                        StepName = $"{currentStep.Name}（加签）",
                        ApproverType = ApprovalApproverType.User,
                        ApproverValue = userId,
                        ApproverUserId = userId,
                        Status = ApprovalTaskStatus.Pending,
                        CreateTime = now
                    };
                    ApplyStepTimeout(newTask, currentStep, now);
                    await taskDataService.EntityCreateAsync(newTask, ct);
                }
            }

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }

        // Notify 模式：post-commit 派发加签通知事件（C3 范式；Participate 建真实任务待办，无需通知事件）
        if (mode == ApprovalAppendMode.Notify)
        {
            var evt = new ApprovalAppendNotifyEvent(
                task.InstanceId, task.Id, task.StepIndex, distinctAppenders, operatedByUserId, remark);
            try
            {
                await localEventBus.PublishAsync(evt);
                logger.LogInformation("加签通知事件已派发: InstanceId={InstanceId}, TaskId={TaskId}, Users={Count}",
                    task.InstanceId, task.Id, distinctAppenders.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "加签通知事件派发失败（不影响加签记录）: InstanceId={InstanceId}", task.InstanceId);
            }
        }
    }

    // ── v0.2.0：抄送（独立实体 + 事件通知）──

    /// <inheritdoc />
    public async Task AddCCAsync(long instanceId, string[] userIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var instance = await instanceDataService.EntityGetAsync(i => i.Id == instanceId, ct)
            ?? throw new InvalidApprovalOperationException($"审批实例 {instanceId} 不存在");

        if (instance.Status is ApprovalInstanceStatus.Approved
            or ApprovalInstanceStatus.Rejected
            or ApprovalInstanceStatus.Withdrawn)
            throw new InvalidApprovalOperationException(
                $"审批实例已终态（{instance.Status}），不可追加抄送");

        var now = DateTime.UtcNow;
        foreach (var userId in userIds.Distinct(StringComparer.Ordinal))
        {
            await ccDataService.EntityCreateAsync(new ApprovalCCEntity
            {
                InstanceId = instanceId,
                UserId = userId,
                Position = ApprovalCCPosition.Finish, // 运行中追加→完成时通知
                CreateTime = now
            }, ct);
        }
    }

    // ── v0.2.0：超时系统动作（C1——同程序集 ApprovalTimeoutService 调用）──

    /// <summary>
    /// 系统转交（超时 Transfer 动作）——跳过 C1 身份校验（系统触发非用户操作）但保留状态机校验。
    /// <para>审计：原任务 Transferred + TransferredTo=目标（D15 不冒充审批人）；新任务继承超时配置（P4）。</para>
    /// </summary>
    internal Task TransferAsSystemAsync(long taskId, string toUserId, CancellationToken ct)
        => TransferCoreAsync(taskId, toUserId, ct);

    /// <summary>
    /// 系统通过（超时 Approve 动作）——跳过 C1 身份校验但保留状态机校验。
    /// <para>审计：ApprovedBy=<see cref="SystemTimeoutActor"/>（D15 不冒充审批人）。</para>
    /// </summary>
    internal Task ApproveAsSystemAsync(long taskId, string? comment, CancellationToken ct)
        => ApproveCoreAsync(taskId, SystemTimeoutActor, comment, ct);

    /// <summary>
    /// 系统驳回（超时 Reject 动作）——跳过 C1 身份校验但保留状态机校验。
    /// <para>审计：ApprovedBy=<see cref="SystemTimeoutActor"/>（D15 不冒充审批人）。</para>
    /// </summary>
    internal Task RejectAsSystemAsync(long taskId, string reason, CancellationToken ct)
        => RejectCoreAsync(taskId, SystemTimeoutActor, reason, ct);

    /// <summary>
    /// 系统跳转（超时 Jump 动作）——跳过 C1 身份校验但保留状态机校验。
    /// <para>校验（P2）：目标步骤 0&lt;=index&lt;steps.Count 且 index&gt;当前步骤（越界/向后/等于当前拒绝）→
    /// 当前步骤所有 Pending 任务 Completed + 实例 CurrentStepIndex=目标 + 创建目标步骤任务。</para>
    /// </summary>
    internal async Task JumpAsSystemAsync(long taskId, CancellationToken ct)
    {
        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

        // 状态机：Pending 才可跳转
        ValidateTaskTransition(task.Status, ApprovalTaskStatus.Completed);

        var instance = await instanceDataService.EntityGetAsync(i => i.Id == task.InstanceId, ct)
            ?? throw new InvalidApprovalOperationException($"审批实例 {task.InstanceId} 不存在");
        var steps = ApprovalStepDefinitionSerializer.Deserialize(
            (await flowDataService.EntityGetAsync(f => f.Id == instance.FlowId, ct))?.StepsJson ?? "[]");

        // P2：目标步骤校验（越界/向后/等于当前拒绝）
        var targetIndex = task.TimeoutJumpToStepIndex;
        if (targetIndex is not int jumpIndex)
            throw new InvalidApprovalOperationException($"任务 {task.Id} 未配置跳转目标步骤（TimeoutJumpToStepIndex 为空）");
        if (jumpIndex < 0 || jumpIndex >= steps.Count)
            throw new InvalidApprovalOperationException($"跳转目标步骤 {jumpIndex} 越界（步骤数 {steps.Count}）");
        if (jumpIndex <= task.StepIndex)
            throw new InvalidApprovalOperationException(
                $"跳转目标步骤 {jumpIndex} 必须晚于当前步骤 {task.StepIndex}（向后/等于当前拒绝）");

        // 事务包裹：当前步骤清理 + 目标步骤任务创建原子提交
        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            // 当前步骤所有 Pending 任务标记 Completed
            var pendingTasks = await taskDataService.EntitySelectAsync(
                t => t.InstanceId == instance.Id && t.StepIndex == task.StepIndex
                     && t.Status == ApprovalTaskStatus.Pending,
                0, int.MaxValue, q => q, ct);
            foreach (var pendingTask in pendingTasks)
            {
                pendingTask.Status = ApprovalTaskStatus.Completed;
                await taskDataService.EntityUpdateAsync(pendingTask, ct);
            }

            // 实例推进到目标步骤 + 创建目标步骤任务
            instance.CurrentStepIndex = jumpIndex;
            await instanceDataService.EntityUpdateAsync(instance, ct);
            await CreateTasksForStepAsync(instance, steps[jumpIndex], ct);

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }
    }

    // ── 内部方法 ──

    /// <summary>
    /// 审批通过核心逻辑（C1 身份校验由调用方负责——用户路径 ApproveAsync / 系统路径 ApproveAsSystemAsync）。
    /// <para>任务通过→步骤判定→下步/完成 + 终态释放 IsActive + 事件派发（post-commit）。</para>
    /// </summary>
    private async Task ApproveCoreAsync(long taskId, string approverUserId, string? comment, CancellationToken ct)
    {
        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

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

            // 查同步骤全部任务（P2-2：int.MaxValue 去 1000 上限——加签后同步骤任务数可超 1000，All 模式漏算风险）
            var allTasksForStep = await taskDataService.EntitySelectAsync(
                t => t.InstanceId == instance.Id && t.StepIndex == task.StepIndex,
                0, int.MaxValue, q => q.OrderBy(t => t.Id), ct);

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

    /// <summary>
    /// 驳回核心逻辑（C1 身份校验由调用方负责——用户路径 RejectAsync / 系统路径 RejectAsSystemAsync）。
    /// </summary>
    private async Task RejectCoreAsync(long taskId, string approverUserId, string reason, CancellationToken ct)
    {
        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

        // 状态机：Pending→Rejected
        ValidateTaskTransition(task.Status, ApprovalTaskStatus.Rejected);

        // 事务包裹：任务状态 + 同步骤清理 + 实例终态原子提交
        ApprovalInstanceEntity? terminalInstance = null;

        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            task.Status = ApprovalTaskStatus.Rejected;
            task.ApprovedAt = DateTime.UtcNow; // 命名沿用 v0.1.0（P2-3 P 级可选优化，行为不变）
            task.ApprovedBy = approverUserId;
            task.Comment = reason;
            await taskDataService.EntityUpdateAsync(task, ct);

            // 获取实例
            var instance = await instanceDataService.EntityGetAsync(i => i.Id == task.InstanceId, ct)
                ?? throw new InvalidApprovalOperationException($"审批实例 {task.InstanceId} 不存在");

            // 将同步骤其余 Pending 任务标记 Completed
            var allTasksForStep = await taskDataService.EntitySelectAsync(
                t => t.InstanceId == instance.Id && t.StepIndex == task.StepIndex,
                0, int.MaxValue, q => q.OrderBy(t => t.Id), ct);
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

    /// <summary>
    /// 转交核心逻辑（C1 身份校验由调用方负责——用户路径 TransferAsync / 系统路径 TransferAsSystemAsync）。
    /// <para>原任务 Transferred + 新任务 Pending（ApproverType=User，继承当前步骤超时配置 P4）。</para>
    /// </summary>
    private async Task TransferCoreAsync(long taskId, string toUserId, CancellationToken ct)
    {
        var task = await taskDataService.EntityGetAsync(t => t.Id == taskId, ct)
            ?? throw new InvalidApprovalOperationException($"审批任务 {taskId} 不存在");

        // 状态机：Pending→Transferred
        ValidateTaskTransition(task.Status, ApprovalTaskStatus.Transferred);

        // 事务包裹：旧任务状态 + 新任务创建原子提交
        using var scope = await transactionManager.BeginAsync(ct: ct);
        try
        {
            task.Status = ApprovalTaskStatus.Transferred;
            task.TransferredTo = toUserId;
            await taskDataService.EntityUpdateAsync(task, ct);

            // 获取实例（获取当前步骤名 + 超时配置）
            var instance = await instanceDataService.EntityGetAsync(i => i.Id == task.InstanceId, ct)
                ?? throw new InvalidApprovalOperationException($"审批实例 {task.InstanceId} 不存在");
            var steps = ApprovalStepDefinitionSerializer.Deserialize(
                (await flowDataService.EntityGetAsync(f => f.Id == instance.FlowId, ct))?.StepsJson ?? "[]");
            var currentStep = steps[task.StepIndex];

            // 创建新任务给 toUserId（ApproverType=User，ApproverValue=toUserId，ApproverUserId=toUserId）
            var now = DateTime.UtcNow;
            var newTask = new ApprovalTaskEntity
            {
                InstanceId = task.InstanceId,
                StepIndex = task.StepIndex,
                StepName = currentStep.Name,
                ApproverType = ApprovalApproverType.User,
                ApproverValue = toUserId,
                ApproverUserId = toUserId,
                Status = ApprovalTaskStatus.Pending,
                CreateTime = now
            };
            ApplyStepTimeout(newTask, currentStep, now); // P4：转交新任务继承步骤超时配置
            await taskDataService.EntityCreateAsync(newTask, ct);

            await scope.CommitAsync(ct);
        }
        catch
        {
            await scope.RollbackAsync(ct);
            throw;
        }
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
    /// <para>v0.2.0：终态时同步派发 Finish 位置抄送事件（P7——Withdrawn 因前置 return 天然不触发）。</para>
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

        // v0.2.0：终态（Approved/Rejected）时通知 Finish/StartFinish 位置抄送人（P7——Withdrawn 在此方法前置 return 排除）
        await RaiseCcEventAsync(instance, terminalStatus, ApprovalCCPosition.Finish, ct);
    }

    /// <summary>
    /// 派发抄送事件（post-commit）——查询实例对应位置抄送记录并聚合派发。
    /// <para>Start 位置：Position==Start 或 StartFinish 的抄送人；Finish 位置：Position==Finish 或 StartFinish。</para>
    /// </summary>
    private async Task RaiseCcEventAsync(ApprovalInstanceEntity instance, ApprovalInstanceStatus? result,
        ApprovalCCPosition position, CancellationToken ct)
    {
        var ccs = await ccDataService.EntitySelectAsync(
            c => c.InstanceId == instance.Id
                 && (position == ApprovalCCPosition.Start
                     ? (c.Position == ApprovalCCPosition.Start || c.Position == ApprovalCCPosition.StartFinish)
                     : (c.Position == ApprovalCCPosition.Finish || c.Position == ApprovalCCPosition.StartFinish)),
            0, int.MaxValue, q => q.OrderBy(c => c.Id), ct);
        if (ccs.Count == 0)
            return;

        var userIds = ccs.Select(c => c.UserId).Distinct(StringComparer.Ordinal).ToList();
        var evt = new ApprovalCcNotifiedEvent(
            instance.Id, instance.BusinessType, instance.BusinessId, result, position, userIds);
        try
        {
            await localEventBus.PublishAsync(evt);
            logger.LogInformation("抄送事件已派发: InstanceId={InstanceId}, Position={Position}, Users={Count}",
                instance.Id, position, userIds.Count);
        }
        catch (Exception ex)
        {
            // 事件 handler 异常不影响审批结果
            logger.LogWarning(ex, "抄送事件派发失败（不影响审批结果）: InstanceId={InstanceId}", instance.Id);
        }
    }

    /// <summary>
    /// 为指定步骤创建审批任务（解析审批人 → 建任务，ApproverUserId = 解析结果快照 P1-4）。
    /// <para>v0.2.0：任务创建时写入步骤超时配置快照（TimeoutAt=CreateTime+TimeoutMinutes + 动作快照，P4）。</para>
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

        var now = DateTime.UtcNow;
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
                CreateTime = now
            };
            ApplyStepTimeout(task, step, now);
            await taskDataService.EntityCreateAsync(task, ct);
        }
    }

    /// <summary>
    /// 应用步骤超时配置到任务（TimeoutAt=创建时间+TimeoutMinutes + 动作快照）。
    /// <para>TimeoutMinutes 为空或 &lt;=0 视为不超时（TimeoutAt=null，超时服务跳过）。
    /// 加签/转交/步骤创建的新任务均经此写入（P4 评审——同规则继承）。</para>
    /// </summary>
    private static void ApplyStepTimeout(ApprovalTaskEntity task, ApprovalStepDefinition step, DateTime createTime)
    {
        if (step.TimeoutMinutes is not int minutes || minutes <= 0)
            return;

        task.TimeoutAt = createTime.AddMinutes(minutes);
        task.TimeoutAction = step.TimeoutAction;
        task.TimeoutTransferToUserId = step.TimeoutTransferToUserId;
        task.TimeoutJumpToStepIndex = step.TimeoutJumpToStepIndex;
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
