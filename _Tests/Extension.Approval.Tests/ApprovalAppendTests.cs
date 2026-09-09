using System;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.Approval;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// v0.2.0 加签测试——D4-D6（钉钉步骤内模型：当前步骤并行追加审批人）。
/// <para>D4: Participate 建新任务（StepName 含加签标记 + TimeoutAt 继承步骤配置 P4）+ 原任务保持 Pending + 步骤判定复用
/// D5: Notify 仅记录 + 不建任务 + 发布 ApprovalAppendNotifyEvent（C3）
/// D6: 去重（C2）：加签已存在审批人 / 同批 appenderUserIds 含重复 / 正常加签新人 + 身份校验</para>
/// </summary>
public class ApprovalAppendTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly ApprovalTestHost _host;

    public ApprovalAppendTests()
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

    /// <summary>建流程并提交，返回实例 ID + 首个任务。</summary>
    private async Task<(long InstanceId, ApprovalTaskEntity Task)> StartSubmittedAsync(
        ApprovalStepDefinition[] steps, string flowCode)
    {
        await _host.ApprovalService.CreateFlowAsync(flowCode, "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", Guid.NewGuid().ToString("N"), flowCode, "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);
        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        return (instanceId, task);
    }

    [Fact]
    public async Task D4_AppendParticipate_ShouldCreateAppendTask()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "财务", ApprovalApproverType.User, "fin") };
        var (_, task) = await StartSubmittedAsync(steps, "append_flow");

        await _host.ApprovalService.AppendApproverAsync(task.Id, "fin", new[] { "legal" }, remark: "法务确认");

        // 加签记录
        var appends = await _host.AppendDataService.GetByInstanceAsync(task.InstanceId);
        Assert.Single(appends);
        Assert.Equal("legal", appends[0].UserId);
        Assert.Equal("fin", appends[0].OperatedByUserId);
        Assert.Equal(ApprovalAppendMode.Participate, appends[0].AppendMode);

        // 新任务（StepName 含加签标记 + Pending）
        var appendTasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == task.InstanceId && t.ApproverUserId == "legal", 0, 10, q => q, default);
        Assert.Single(appendTasks);
        Assert.Equal("财务（加签）", appendTasks[0].StepName);
        Assert.Equal(ApprovalTaskStatus.Pending, appendTasks[0].Status);
        Assert.Equal(0, appendTasks[0].StepIndex);

        // 原任务保持 Pending
        var original = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalTaskStatus.Pending, original!.Status);
    }

    [Fact]
    public async Task D4_AppendParticipate_TimeoutAt_Inherited_FromStepConfig()
    {
        // P4：加签新任务继承当前步骤超时配置（TimeoutAt=创建时间+TimeoutMinutes + 动作快照）
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "财务", ApprovalApproverType.User, "fin",
                TimeoutMinutes: 30, TimeoutAction: ApprovalTimeoutAction.Remind)
        };
        var (_, task) = await StartSubmittedAsync(steps, "append_timeout");

        await _host.ApprovalService.AppendApproverAsync(task.Id, "fin", new[] { "legal" });

        var appendTask = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == task.InstanceId && t.ApproverUserId == "legal", 0, 10, q => q, default)).First();
        Assert.NotNull(appendTask.TimeoutAt);
        // TimeoutAt = 创建时间 + 30 分钟（容差：>= 29 分钟且 <= 30 分钟）
        var diff = appendTask.TimeoutAt!.Value - appendTask.CreateTime;
        Assert.True(diff >= TimeSpan.FromMinutes(29) && diff <= TimeSpan.FromMinutes(30),
            $"TimeoutAt-CreateTime 应约为 30 分钟，实际 {diff}");
        Assert.Equal(ApprovalTimeoutAction.Remind, appendTask.TimeoutAction);
    }

    [Fact]
    public async Task D4_Append_AnyMode_StepJudgment_Reused()
    {
        // Any 模式：加签人通过 → 步骤即完成（步骤判定复用既有 Any/All 逻辑）
        var steps = new[] { new ApprovalStepDefinition(0, "财务", ApprovalApproverType.User, "fin", ApprovalMode.Any) };
        var (instanceId, task) = await StartSubmittedAsync(steps, "append_any");

        await _host.ApprovalService.AppendApproverAsync(task.Id, "fin", new[] { "legal" });

        var appendTask = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId && t.ApproverUserId == "legal", 0, 10, q => q, default)).First();
        await _host.ApprovalService.ApproveAsync(appendTask.Id, "legal");

        // Any：任一通过即步骤完成 → 实例 Approved + 原任务标记 Completed
        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Approved, instance!.Status);
        var original = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalTaskStatus.Completed, original!.Status);
    }

    [Fact]
    public async Task D5_AppendNotify_ShouldRecordOnlyAndPublishEvent()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "财务", ApprovalApproverType.User, "fin") };
        var (_, task) = await StartSubmittedAsync(steps, "append_notify");
        _host.Events.PublishedEvents.Clear();

        await _host.ApprovalService.AppendApproverAsync(task.Id, "fin", new[] { "auditor" },
            mode: ApprovalAppendMode.Notify, remark: "仅通知");

        // 记录加签（Notify 模式也记录）——GetByInstanceAsync 按 InstanceId 查询（P 级测试修正：原 task.Id 语义错误）
        var appends = await _host.AppendDataService.GetByInstanceAsync(task.InstanceId);
        Assert.Single(appends);
        Assert.Equal(ApprovalAppendMode.Notify, appends[0].AppendMode);

        // 不建任务（auditor 无待办）
        var auditTasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == task.InstanceId && t.ApproverUserId == "auditor", 0, 10, q => q, default);
        Assert.Empty(auditTasks);

        // C3：发布 ApprovalAppendNotifyEvent
        var evt = _host.Events.PublishedEvents.OfType<ApprovalAppendNotifyEvent>().FirstOrDefault();
        Assert.NotNull(evt);
        Assert.Equal(task.InstanceId, evt!.InstanceId);
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(0, evt.StepIndex);
        Assert.Equal(new[] { "auditor" }, evt.NotifyUserIds);
        Assert.Equal("fin", evt.OperatedByUserId);
        Assert.Equal("仅通知", evt.Remark);
    }

    [Fact]
    public async Task D6_Append_ExistingStepApprover_ShouldThrow()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "财务", ApprovalApproverType.User, "fin") };
        var (_, task) = await StartSubmittedAsync(steps, "append_dup");

        // C2：加签已存在审批人（fin 是当前步骤审批人）→ 拒绝
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.AppendApproverAsync(task.Id, "fin", new[] { "fin" }));
    }

    [Fact]
    public async Task D6_Append_DuplicateInBatch_ShouldDedupe()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "财务", ApprovalApproverType.User, "fin") };
        var (_, task) = await StartSubmittedAsync(steps, "append_batch_dup");

        // C2：同批 appenderUserIds 含重复 → Distinct 后仅建一条
        await _host.ApprovalService.AppendApproverAsync(task.Id, "fin", new[] { "legal", "legal" });

        var legalTasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == task.InstanceId && t.ApproverUserId == "legal", 0, 10, q => q, default);
        Assert.Single(legalTasks);

        var appends = await _host.AppendDataService.GetByInstanceAsync(task.InstanceId);
        Assert.Single(appends); // 记录也去重
    }

    [Fact]
    public async Task D6_Append_NonOperator_ShouldThrow()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "财务", ApprovalApproverType.User, "fin") };
        var (_, task) = await StartSubmittedAsync(steps, "append_auth");

        // C1 身份校验：非当前审批人不可加签
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.AppendApproverAsync(task.Id, "wrong_user", new[] { "legal" }));
    }

    [Fact]
    public async Task D6_Append_OnCompletedTask_ShouldThrow()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "财务", ApprovalApproverType.User, "fin") };
        var (_, task) = await StartSubmittedAsync(steps, "append_completed");
        await _host.ApprovalService.ApproveAsync(task.Id, "fin");

        // 任务已非 Pending → 加签拒绝（状态机校验）
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.AppendApproverAsync(task.Id, "fin", new[] { "legal" }));
    }
}
