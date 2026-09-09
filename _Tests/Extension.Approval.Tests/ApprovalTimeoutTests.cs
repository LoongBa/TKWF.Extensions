using System;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.Approval;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// v0.2.0 超时自动处理测试——D9-D11 + D15（钉钉 5 动作模型 + 后台任务触发）。
/// <para>D9: Remind 事件 + Transfer（系统身份）+ Jump（含目标校验 P2）+ Approve/Reject（系统身份）
/// D10: 扫描即占位（条件更新影响行数=0 跳过，P3）+ TimeoutProcessed 后不重复
/// D11: 步骤级 TimeoutMinutes 生效 → TimeoutAt 计算正确（含加签/转交新任务继承 P4）
/// D15: 系统身份审计 ApprovedBy="system:timeout"（C1）</para>
/// </summary>
public class ApprovalTimeoutTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly ApprovalTestHost _host;

    public ApprovalTimeoutTests()
    {
        _fsql = ApprovalTestSupport.CreateInMemoryFreeSql();
        ApprovalTestSupport.SyncStructure(_fsql);
        _host = ApprovalTestSupport.Build(_fsql);
    }

    public void Dispose()
    {
        _host.Dispose();
        _fsql.Dispose();
    }

    /// <summary>建流程并提交，把任务 TimeoutAt 回拨到过去使其超期，返回任务。</summary>
    private async Task<ApprovalTaskEntity> StartOverdueTaskAsync(
        ApprovalStepDefinition[] steps, string flowCode, Action<ApprovalTaskEntity>? configure = null)
    {
        await _host.ApprovalService.CreateFlowAsync(flowCode, "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", Guid.NewGuid().ToString("N"), flowCode, "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        configure?.Invoke(task);
        task.TimeoutAt = DateTime.UtcNow.AddMinutes(-1); // 已超期
        task.TimeoutProcessed = false;
        await _host.TaskDataService.EntityUpdateAsync(task, default);
        return task;
    }

    [Fact]
    public async Task D9_Remind_ShouldPublishTimeoutEvent()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Remind)
        };
        var task = await StartOverdueTaskAsync(steps, "timeout_remind");
        _host.Events.PublishedEvents.Clear();

        var processed = await _host.TimeoutService.ProcessTimeoutTasksAsync();

        Assert.Equal(1, processed);
        var evt = _host.Events.PublishedEvents.OfType<ApprovalTaskTimeoutEvent>().FirstOrDefault();
        Assert.NotNull(evt);
        Assert.Equal(task.Id, evt!.TaskId);
        Assert.Equal(ApprovalTimeoutAction.Remind, evt.Action);
        Assert.Equal("Biz", evt.BusinessType);
        Assert.Null(evt.TransferToUserId);

        // 任务保持 Pending + TimeoutProcessed=true
        var updated = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalTaskStatus.Pending, updated!.Status);
        Assert.True(updated.TimeoutProcessed);
    }

    [Fact]
    public async Task D9_Transfer_System_ShouldTransferTask()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Transfer, TimeoutTransferToUserId: "deputy")
        };
        var task = await StartOverdueTaskAsync(steps, "timeout_transfer");

        await _host.TimeoutService.ProcessTimeoutTasksAsync();

        // 原任务 Transferred + TransferredTo=目标
        var original = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalTaskStatus.Transferred, original!.Status);
        Assert.Equal("deputy", original.TransferredTo);

        // 新任务 Pending（继承超时配置 P4）
        var newTasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == task.InstanceId && t.ApproverUserId == "deputy", 0, 10, q => q, default);
        Assert.Single(newTasks);
        Assert.Equal(ApprovalTaskStatus.Pending, newTasks[0].Status);
        Assert.NotNull(newTasks[0].TimeoutAt); // P4：转交新任务继承步骤超时配置
        Assert.Equal(ApprovalTimeoutAction.Transfer, newTasks[0].TimeoutAction);
        Assert.Equal("deputy", newTasks[0].TimeoutTransferToUserId);
    }

    [Fact]
    public async Task D9_Jump_System_ShouldJumpToTargetStep()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Jump, TimeoutJumpToStepIndex: 1),
            new ApprovalStepDefinition(1, "财务", ApprovalApproverType.User, "fin")
        };
        var task = await StartOverdueTaskAsync(steps, "timeout_jump");

        await _host.TimeoutService.ProcessTimeoutTasksAsync();

        // 当前步骤（0）任务 Completed
        var original = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalTaskStatus.Completed, original!.Status);

        // 实例推进到步骤 1 + 建目标步骤任务
        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == task.InstanceId);
        Assert.Equal(1, instance!.CurrentStepIndex);
        var step1Tasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instance.Id && t.StepIndex == 1, 0, 10, q => q, default);
        Assert.Single(step1Tasks);
        Assert.Equal("fin", step1Tasks[0].ApproverUserId);

        // 目标步骤可正常审批完成
        await _host.ApprovalService.ApproveAsync(step1Tasks[0].Id, "fin");
        instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == task.InstanceId);
        Assert.Equal(ApprovalInstanceStatus.Approved, instance!.Status);
    }

    [Fact]
    public async Task D9_Jump_InvalidTarget_EqualCurrent_ShouldThrow()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Jump, TimeoutJumpToStepIndex: 0),
            new ApprovalStepDefinition(1, "财务", ApprovalApproverType.User, "fin")
        };
        await StartOverdueTaskAsync(steps, "timeout_jump_equal");

        // P2：等于当前步骤拒绝——动作失败但占位生效（processed=1，不抛到扫描层）
        var processed = await _host.TimeoutService.ProcessTimeoutTasksAsync();
        Assert.Equal(1, processed);

        // 任务保持 Pending（跳转未执行）
        var tasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.Status == ApprovalTaskStatus.Pending, 0, 10, q => q, default);
        Assert.NotEmpty(tasks);
    }

    [Fact]
    public async Task D9_Jump_InvalidTarget_OutOfRange_ShouldThrow()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Jump, TimeoutJumpToStepIndex: 5),
            new ApprovalStepDefinition(1, "财务", ApprovalApproverType.User, "fin")
        };
        await StartOverdueTaskAsync(steps, "timeout_jump_oob");

        var processed = await _host.TimeoutService.ProcessTimeoutTasksAsync();
        Assert.Equal(1, processed); // 占位成功（防重扫），动作失败记录日志

        var tasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.Status == ApprovalTaskStatus.Pending, 0, 10, q => q, default);
        Assert.NotEmpty(tasks); // 跳转未执行——仍 Pending
    }

    [Fact]
    public async Task D9_Approve_System_ShouldApproveWithSystemAudit()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Approve)
        };
        var task = await StartOverdueTaskAsync(steps, "timeout_approve");

        await _host.TimeoutService.ProcessTimeoutTasksAsync();

        // 实例 Approved（系统自动通过）
        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == task.InstanceId);
        Assert.Equal(ApprovalInstanceStatus.Approved, instance!.Status);

        // D15：审计 ApprovedBy="system:timeout"（不冒充审批人）
        var updated = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalTaskStatus.Approved, updated!.Status);
        Assert.Equal(ApprovalManager.SystemTimeoutActor, updated.ApprovedBy);
    }

    [Fact]
    public async Task D9_Reject_System_ShouldRejectWithSystemAudit()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Reject)
        };
        var task = await StartOverdueTaskAsync(steps, "timeout_reject");

        await _host.TimeoutService.ProcessTimeoutTasksAsync();

        // 实例 Rejected（系统自动驳回）
        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == task.InstanceId);
        Assert.Equal(ApprovalInstanceStatus.Rejected, instance!.Status);
        Assert.NotNull(instance.RejectedAt); // C5 验证：RejectedAt 正确写入

        // D15：审计 ApprovedBy="system:timeout"
        var updated = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalTaskStatus.Rejected, updated!.Status);
        Assert.Equal(ApprovalManager.SystemTimeoutActor, updated.ApprovedBy);
    }

    [Fact]
    public async Task D10_Processed_ShouldNotReprocess()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Remind)
        };
        await StartOverdueTaskAsync(steps, "timeout_once");

        // 第一次处理 → 1 个
        var first = await _host.TimeoutService.ProcessTimeoutTasksAsync();
        Assert.Equal(1, first);

        // 第二次 → 0（TimeoutProcessed 已置位，扫描即排除；P3 防重）
        var second = await _host.TimeoutService.ProcessTimeoutTasksAsync();
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task D10_ClaimTimeout_NonPending_ShouldReturnZero()
    {
        // P3：条件更新影响行数=0 跳过——任务已非 Pending 时 ClaimTimeoutAsync 返回 0
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Remind)
        };
        await _host.ApprovalService.CreateFlowAsync("claim_flow", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", Guid.NewGuid().ToString("N"), "claim_flow", "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);
        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();

        // 已通过（非 Pending）——ClaimTimeoutAsync 拒绝占位
        await _host.ApprovalService.ApproveAsync(task.Id, "mgr");
        var claimed = await _host.TaskDataService.ClaimTimeoutAsync(task.Id);
        Assert.Equal(0, claimed);
    }

    [Fact]
    public async Task D11_TimeoutAt_Calculated_FromStepConfig()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr",
                TimeoutMinutes: 45, TimeoutAction: ApprovalTimeoutAction.Remind)
        };
        await _host.ApprovalService.CreateFlowAsync("timeout_calc", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", Guid.NewGuid().ToString("N"), "timeout_calc", "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);

        // D11：步骤级 TimeoutMinutes=45 → 任务 TimeoutAt = CreateTime+45min（P4 常规步骤创建路径）
        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        Assert.NotNull(task.TimeoutAt);
        var diff = task.TimeoutAt!.Value - task.CreateTime;
        Assert.True(diff >= TimeSpan.FromMinutes(44) && diff <= TimeSpan.FromMinutes(45),
            $"TimeoutAt-CreateTime 应约为 45 分钟，实际 {diff}");
        Assert.Equal(ApprovalTimeoutAction.Remind, task.TimeoutAction);
    }

    [Fact]
    public async Task D11_NoTimeoutMinutes_ShouldNotSetTimeoutAt()
    {
        // TimeoutMinutes=null（默认）→ 任务 TimeoutAt=null（不超时）
        var steps = new[] { new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr") };
        await _host.ApprovalService.CreateFlowAsync("timeout_none", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", Guid.NewGuid().ToString("N"), "timeout_none", "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        Assert.Null(task.TimeoutAt);
    }
}
