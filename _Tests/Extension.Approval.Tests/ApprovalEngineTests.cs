using System;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.Approval;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// 审批引擎集成测试——覆盖 D1-D10 验收标准。
/// <para>D1: 实体建表 + 唯一约束 + 索引
/// D2: 流程定义 CRUD + Enable/Disable
/// D3: StartAsync 防重复
/// D4: SubmitAsync Draft→Pending + 建首步任务
/// D5: ApproveAsync 审批人身份硬校验 + Any/All 状态迁移
/// D6: RejectAsync → 实例 Rejected 终态 + IsActive=false
/// D7: TransferAsync 转交
/// D8: WithdrawAsync 撤回权限
/// D9: 完成事件派发
/// D10: 查询门面</para>
/// </summary>
public class ApprovalEngineTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly ApprovalTestHost _host;

    public ApprovalEngineTests()
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

    // ── D2: 流程定义 CRUD + Enable/Disable ──

    [Fact]
    public async Task D2_CreateFlow_ShouldReturnId()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "manager1", ApprovalMode.Any)
        };

        var flowId = await _host.ApprovalService.CreateFlowAsync("test_flow", "测试流程", steps, "描述");

        Assert.True(flowId > 0);

        var entity = await _host.FlowDataService.EntityGetAsync(f => f.Id == flowId);
        Assert.NotNull(entity);
        Assert.Equal("test_flow", entity!.Code);
        Assert.Equal("测试流程", entity.Name);
        Assert.True(entity.IsEnabled);
    }

    [Fact]
    public async Task D2_CreateFlow_DuplicateCode_ShouldThrow()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "u1") };

        await _host.ApprovalService.CreateFlowAsync("dup_code", "流程1", steps);
        await Assert.ThrowsAnyAsync<Exception>(
            () => _host.ApprovalService.CreateFlowAsync("dup_code", "流程2", steps));
    }

    [Fact]
    public async Task D2_UpdateFlow_ShouldUpdateFields()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "u1") };
        var flowId = await _host.ApprovalService.CreateFlowAsync("update_flow", "旧名", steps);

        var newSteps = new[] { new ApprovalStepDefinition(0, "总监", ApprovalApproverType.User, "u2") };
        await _host.ApprovalService.UpdateFlowAsync(flowId, "新名", newSteps, "新描述");

        var entity = await _host.FlowDataService.EntityGetAsync(f => f.Id == flowId);
        Assert.Equal("新名", entity!.Name);
        Assert.Equal("新描述", entity.Description);
    }

    [Fact]
    public async Task D2_EnableDisable_ShouldToggleIsEnabled()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "u1") };
        var flowId = await _host.ApprovalService.CreateFlowAsync("toggle_flow", "流程", steps);

        await _host.ApprovalService.DisableFlowAsync(flowId);
        Assert.False((await _host.FlowDataService.EntityGetAsync(f => f.Id == flowId))!.IsEnabled);

        await _host.ApprovalService.EnableFlowAsync(flowId);
        Assert.True((await _host.FlowDataService.EntityGetAsync(f => f.Id == flowId))!.IsEnabled);
    }

    [Fact]
    public async Task D2_DisabledFlow_StartAsync_ShouldThrow()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "u1") };
        var flowId = await _host.ApprovalService.CreateFlowAsync("disabled_flow", "流程", steps);
        await _host.ApprovalService.DisableFlowAsync(flowId);

        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.StartAsync("Biz", "1", "disabled_flow", "submitter"));
    }

    // ── D3: StartAsync 防重复 ──

    [Fact]
    public async Task D3_StartAsync_Duplicate_ShouldThrow()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "u1") };
        await _host.ApprovalService.CreateFlowAsync("dup_start", "流程", steps);

        await _host.ApprovalService.StartAsync("Biz", "1", "dup_start", "user1");
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.StartAsync("Biz", "1", "dup_start", "user1"));
    }

    [Fact]
    public async Task D3_StartAsync_AfterTerminalState_ShouldAllowResubmit()
    {
        var steps = new[] { new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "u1") };
        await _host.ApprovalService.CreateFlowAsync("resubmit", "流程", steps);

        // 第一次提交
        var id1 = await _host.ApprovalService.StartAsync("Biz", "1", "resubmit", "user1");
        await _host.ApprovalService.SubmitAsync(id1);

        // 审批通过（终态 IsActive=false）
        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == id1, 0, 10, q => q, default)).First();
        await _host.ApprovalService.ApproveAsync(task.Id, "u1");

        // 终态后可重新 Start（IsActive 释放约束）
        var id2 = await _host.ApprovalService.StartAsync("Biz", "1", "resubmit", "user1");
        Assert.True(id2 > 0);
        Assert.NotEqual(id1, id2);
    }

    // ── D4: SubmitAsync ──

    [Fact]
    public async Task D4_SubmitAsync_ShouldCreateFirstStepTask()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "manager1")
        };
        await _host.ApprovalService.CreateFlowAsync("submit_flow", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "submit_flow", "user1");

        await _host.ApprovalService.SubmitAsync(instanceId);

        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Pending, instance!.Status);
        Assert.True(instance.SubmittedAt.HasValue);

        var tasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default);
        Assert.Single(tasks);
        Assert.Equal("manager1", tasks[0].ApproverUserId);
        Assert.Equal(ApprovalTaskStatus.Pending, tasks[0].Status);
    }

    // ── D5: ApproveAsync + Any/All ──

    [Fact]
    public async Task D5_ApproveAsync_SingleStep_ShouldComplete()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("approve_single", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "approve_single", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        await _host.ApprovalService.ApproveAsync(task.Id, "mgr", "同意");

        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Approved, instance!.Status);
        Assert.False(instance.IsActive); // C2: 终态释放
        Assert.True(instance.ApprovedAt.HasValue);
    }

    [Fact]
    public async Task D5_ApproveAsync_MultiStep_ShouldProgressToNextStep()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr"),
            new ApprovalStepDefinition(1, "财务", ApprovalApproverType.User, "fin")
        };
        await _host.ApprovalService.CreateFlowAsync("approve_multi", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "approve_multi", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        // 第一步通过
        var task1 = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId && t.StepIndex == 0, 0, 10, q => q, default)).First();
        await _host.ApprovalService.ApproveAsync(task1.Id, "mgr");

        // 应有第二步任务
        var task2List = await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId && t.StepIndex == 1, 0, 10, q => q, default);
        Assert.Single(task2List);

        // 实例仍在 Pending
        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Pending, instance!.Status);

        // 第二步通过→实例 Approved
        await _host.ApprovalService.ApproveAsync(task2List[0].Id, "fin");
        instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Approved, instance!.Status);
        Assert.False(instance.IsActive);
    }

    [Fact]
    public async Task D5_ApproveAsync_AnyMode_ShouldCompleteOnFirstApproval()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "会签组", ApprovalApproverType.User, "user1", ApprovalMode.Any)
        };
        await _host.ApprovalService.CreateFlowAsync("any_mode", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "any_mode", "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);

        // 手动建两个任务（模拟 Role 解析多人，Any 模式）
        await _host.TaskDataService.EntityCreateAsync(new ApprovalTaskEntity
        {
            InstanceId = instanceId, StepIndex = 0, StepName = "会签组",
            ApproverType = ApprovalApproverType.User, ApproverValue = "user2",
            ApproverUserId = "user2", Status = ApprovalTaskStatus.Pending
        });

        // user1 通过→Any 模式直接完成
        var task1 = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId && t.ApproverUserId == "user1", 0, 10, q => q, default)).First();
        await _host.ApprovalService.ApproveAsync(task1.Id, "user1");

        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Approved, instance!.Status);

        // user2 的任务应标记 Completed
        var task2 = await _host.TaskDataService.EntityGetAsync(t => t.ApproverUserId == "user2");
        Assert.Equal(ApprovalTaskStatus.Completed, task2!.Status);
    }

    [Fact]
    public async Task D5_ApproveAsync_AllMode_ShouldWaitForAll()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "会签组", ApprovalApproverType.User, "user1", ApprovalMode.All)
        };
        await _host.ApprovalService.CreateFlowAsync("all_mode", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "all_mode", "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);

        // 手动建第二个任务
        await _host.TaskDataService.EntityCreateAsync(new ApprovalTaskEntity
        {
            InstanceId = instanceId, StepIndex = 0, StepName = "会签组",
            ApproverType = ApprovalApproverType.User, ApproverValue = "user2",
            ApproverUserId = "user2", Status = ApprovalTaskStatus.Pending
        });

        // user1 通过→All 模式未完成
        var task1 = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId && t.ApproverUserId == "user1", 0, 10, q => q, default)).First();
        await _host.ApprovalService.ApproveAsync(task1.Id, "user1");

        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Pending, instance!.Status); // 仍在 Pending

        // user2 通过→All 模式完成
        var task2 = await _host.TaskDataService.EntityGetAsync(t => t.ApproverUserId == "user2");
        await _host.ApprovalService.ApproveAsync(task2!.Id, "user2");
        instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Approved, instance!.Status);
    }

    [Fact]
    public async Task D5_ApproveAsync_C1_NonApprover_ShouldThrow()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("c1_check", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "c1_check", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();

        // 非任务审批人操作→C1 身份硬校验失败
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.ApproveAsync(task.Id, "wrong_user"));
    }

    // ── D6: RejectAsync ──

    [Fact]
    public async Task D6_RejectAsync_ShouldMarkRejected()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("reject_flow", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "reject_flow", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        await _host.ApprovalService.RejectAsync(task.Id, "mgr", "不通过");

        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Rejected, instance!.Status);
        Assert.False(instance.IsActive); // C2: 终态释放
        Assert.Equal("不通过", instance.Reason);
    }

    [Fact]
    public async Task D6_ApproveAsync_OnTerminalState_ShouldThrow()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("terminal_guard", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "terminal_guard", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        await _host.ApprovalService.ApproveAsync(task.Id, "mgr"); // Approved 终态

        // 终态任务再操作→非法迁移
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.ApproveAsync(task.Id, "mgr"));
    }

    // ── D7: TransferAsync ──

    [Fact]
    public async Task D7_TransferAsync_ShouldCreateNewTask()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("transfer_flow", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "transfer_flow", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        await _host.ApprovalService.TransferAsync(task.Id, "mgr", "deputy");

        // 原任务 Transferred
        var originalTask = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalTaskStatus.Transferred, originalTask!.Status);
        Assert.Equal("deputy", originalTask.TransferredTo);

        // 新任务 Pending
        var newTasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId && t.ApproverUserId == "deputy",
            0, 10, q => q, default);
        Assert.Single(newTasks);
        Assert.Equal(ApprovalTaskStatus.Pending, newTasks[0].Status);
    }

    [Fact]
    public async Task D7_TransferAsync_C1_NonApprover_ShouldThrow()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("transfer_c1", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "transfer_c1", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();

        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.TransferAsync(task.Id, "wrong_user", "deputy"));
    }

    // ── D8: WithdrawAsync ──

    [Fact]
    public async Task D8_WithdrawAsync_Draft_ShouldWork()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("withdraw_draft", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "withdraw_draft", "submitter");

        await _host.ApprovalService.WithdrawAsync(instanceId, "submitter");

        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Withdrawn, instance!.Status);
        Assert.False(instance.IsActive);
    }

    [Fact]
    public async Task D8_WithdrawAsync_Pending_ShouldWork()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("withdraw_pending", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "withdraw_pending", "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);

        await _host.ApprovalService.WithdrawAsync(instanceId, "submitter");

        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Withdrawn, instance!.Status);
        Assert.False(instance.IsActive);
    }

    [Fact]
    public async Task D8_WithdrawAsync_P1_1_NonSubmitter_ShouldThrow()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("withdraw_auth", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "withdraw_auth", "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);

        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.WithdrawAsync(instanceId, "wrong_user"));
    }

    [Fact]
    public async Task D8_WithdrawAsync_OnTerminalState_ShouldThrow()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("withdraw_terminal", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "withdraw_terminal", "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        await _host.ApprovalService.ApproveAsync(task.Id, "mgr"); // 终态

        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.WithdrawAsync(instanceId, "submitter"));
    }

    // ── D9: 完成事件派发 ──

    [Fact]
    public async Task D9_Complete_ShouldPublishEvent()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("event_flow", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Expense", "exp-001", "event_flow", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        _host.Events.PublishedEvents.Clear();

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        await _host.ApprovalService.ApproveAsync(task.Id, "mgr");

        // C3: CommitAsync 后 PublishAsync
        var evt = _host.Events.PublishedEvents.OfType<ApprovalCompletedEvent>().FirstOrDefault();
        Assert.NotNull(evt);
        Assert.Equal(instanceId, evt!.InstanceId);
        Assert.Equal("Expense", evt.BusinessType);
        Assert.Equal("exp-001", evt.BusinessId);
        Assert.Equal(ApprovalInstanceStatus.Approved, evt.Result);
    }

    [Fact]
    public async Task D9_Reject_ShouldPublishEvent()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("reject_event", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Leave", "lr-001", "reject_event", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        _host.Events.PublishedEvents.Clear();

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        await _host.ApprovalService.RejectAsync(task.Id, "mgr", "不同意");

        var evt = _host.Events.PublishedEvents.OfType<ApprovalCompletedEvent>().FirstOrDefault();
        Assert.NotNull(evt);
        Assert.Equal(ApprovalInstanceStatus.Rejected, evt!.Result);
        Assert.Equal("不同意", evt.Reason);
    }

    // ── D10: 查询门面 ──

    [Fact]
    public async Task D10_GetInstancesAsync_ShouldReturnPaged()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("query_flow", "流程", steps);
        await _host.ApprovalService.StartAsync("Biz", "1", "query_flow", "user1");
        await _host.ApprovalService.StartAsync("Biz", "2", "query_flow", "user2");

        var result = await _host.QueryService.GetInstancesAsync(new ApprovalInstanceQueryInput
        {
            BusinessType = "Biz",
            Take = 10
        });

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task D10_GetPendingTasksAsync_ShouldReturnUserTasks()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("task_query", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "task_query", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var result = await _host.QueryService.GetPendingTasksAsync(new ApprovalTaskQueryInput
        {
            ApproverUserId = "mgr"
        });

        Assert.Equal(1, result.Total);
        Assert.Equal("mgr", result.Items[0].ApproverUserId);
    }

    [Fact]
    public async Task D10_GetInstanceDetailAsync_ShouldIncludeTasks()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr"),
            new ApprovalStepDefinition(1, "财务", ApprovalApproverType.User, "fin")
        };
        await _host.ApprovalService.CreateFlowAsync("detail_flow", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "detail_flow", "user1",
            businessDataJson: "{\"amount\":100}");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var detail = await _host.QueryService.GetInstanceDetailAsync(instanceId);

        Assert.NotNull(detail);
        Assert.Equal(instanceId, detail!.Id);
        Assert.Equal("{\"amount\":100}", detail.BusinessDataJson);
        Assert.Single(detail.Tasks); // 仅首步任务
    }

    // ── D5 非法迁移异常 ──

    [Fact]
    public async Task D5_SubmitAsync_OnNonDraft_ShouldThrow()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("invalid_submit", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "invalid_submit", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId); // Draft→Pending

        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.SubmitAsync(instanceId)); // Pending→Pending 非法
    }

    // ── D12: v0.1.0 遗留修复（P2-1~P2-6/P2-7）──

    [Fact]
    public async Task D12_GetInstancesAsync_DBPagination_ShouldReturnPageAndTotal()
    {
        // P2-1/P5：DB 级分页——页数据 skip/take 下推 + total 独立 count（不再 100k 全量内存分页）
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("db_page", "流程", steps);
        for (var i = 0; i < 5; i++)
            await _host.ApprovalService.StartAsync("Biz", $"page-{i}", "db_page", "user1");

        var result = await _host.QueryService.GetInstancesAsync(new ApprovalInstanceQueryInput
        {
            BusinessType = "Biz",
            Skip = 2,
            Take = 2
        });

        Assert.Equal(5, result.Total); // total 独立 count（页数据之外）
        Assert.Equal(2, result.Items.Count); // 页数据精确切片
        // 按 Id 倒序：Id 5,4,3,2,1 → Skip=2 后 [3, 2]
        Assert.Equal(3, result.Items[0].Id);
        Assert.Equal(2, result.Items[1].Id);
    }

    [Fact]
    public async Task D12_GetPendingTasksAsync_DBPagination_ShouldReturnPageAndTotal()
    {
        // P2-1：待办查询 DB 级分页
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("db_page_task", "流程", steps);
        for (var i = 0; i < 4; i++)
        {
            var id = await _host.ApprovalService.StartAsync("Biz", $"pt-{i}", "db_page_task", "user1");
            await _host.ApprovalService.SubmitAsync(id);
        }

        var result = await _host.QueryService.GetPendingTasksAsync(new ApprovalTaskQueryInput
        {
            ApproverUserId = "mgr",
            Skip = 1,
            Take = 2
        });

        Assert.Equal(4, result.Total);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task D12_RejectAsync_ShouldSetRejectedAt()
    {
        // C5：RejectedAt 字段（v0.1.0 已实现——v0.2.0 验证确认）
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("rejected_at", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "rejected_at", "user1");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
        await _host.ApprovalService.RejectAsync(task.Id, "mgr", "驳回");

        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.True(instance!.RejectedAt.HasValue);
        Assert.Null(instance.ApprovedAt);
        Assert.Equal(ApprovalInstanceStatus.Rejected, instance.Status);
    }

    [Fact]
    public async Task D12_WithdrawAsync_Pending_ShouldCompleteAllTasksAtomically()
    {
        // P2-7（P9）：WithdrawAsync 事务包裹——实例 Withdrawn + 全部 Pending 任务 Completed 原子提交
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "会签组", ApprovalApproverType.User, "user1", ApprovalMode.All)
        };
        await _host.ApprovalService.CreateFlowAsync("withdraw_tx", "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", "1", "withdraw_tx", "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);
        // 手动加第二个会签任务
        await _host.TaskDataService.EntityCreateAsync(new ApprovalTaskEntity
        {
            InstanceId = instanceId, StepIndex = 0, StepName = "会签组",
            ApproverType = ApprovalApproverType.User, ApproverValue = "user2",
            ApproverUserId = "user2", Status = ApprovalTaskStatus.Pending
        });

        await _host.ApprovalService.WithdrawAsync(instanceId, "submitter");

        // 实例 Withdrawn + 全部任务 Completed
        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        Assert.Equal(ApprovalInstanceStatus.Withdrawn, instance!.Status);
        Assert.False(instance.IsActive);
        var tasks = await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default);
        Assert.All(tasks, t => Assert.Equal(ApprovalTaskStatus.Completed, t.Status));
    }

    [Fact]
    public async Task D12_CancellationToken_Passthrough_ShouldThrowWhenCanceled()
    {
        // P2-6：CancellationToken 透传断言（取消令牌 → 操作中断）
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr")
        };
        await _host.ApprovalService.CreateFlowAsync("ct_base", "流程", steps);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Record.ExceptionAsync(
            () => _host.ApprovalService.CreateFlowAsync("ct_test", "流程", steps, ct: cts.Token));

        // FreeSql 将 TaskCanceledException 包装为 Exception（"A task was canceled"）——验证取消已透传（异常链含 OperationCanceledException）
        Assert.NotNull(ex);
        var chainHasCancellation = false;
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is OperationCanceledException)
            {
                chainHasCancellation = true;
                break;
            }
        }
        Assert.True(chainHasCancellation, $"ct 取消未透传，异常链: {ex}");
    }

    // ── D13: v0.1.0 零迁移（C4——StartAsync 追加可选参数于 ct 之后；10 方法签名不变）──

    [Fact]
    public void D13_ZeroMigration_StartAsync_ParameterOrder_ShouldKeepCtPosition()
    {
        // C4：ccUserIds 追加于 ct 之后（方案 A）——现有位置传参调用零影响
        var method = typeof(IApprovalService).GetMethod(nameof(IApprovalService.StartAsync))!;
        var parameters = method.GetParameters();

        Assert.Equal(7, parameters.Length); // 6 个原有参数 + 1 个新增可选 ccUserIds（追加于 ct 之后）
        Assert.Equal("businessDataJson", parameters[4].Name);
        Assert.Equal(typeof(CancellationToken), parameters[5].ParameterType);
        Assert.Equal("ct", parameters[5].Name);
        Assert.Equal("ccUserIds", parameters[6].Name);
        Assert.True(parameters[6].IsOptional);
        Assert.True(parameters[5].IsOptional);
    }

    [Fact]
    public void D13_ZeroMigration_OriginalTenMethods_ShouldExist()
    {
        // D13 编译断言：v0.1.0 的 10 个方法签名保持不变（追加的 4 个新方法不破坏既有接口）
        var serviceType = typeof(IApprovalService);
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.CreateFlowAsync)));
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.UpdateFlowAsync)));
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.EnableFlowAsync)));
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.DisableFlowAsync)));
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.StartAsync)));
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.SubmitAsync)));
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.ApproveAsync)));
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.RejectAsync)));
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.TransferAsync)));
        Assert.NotNull(serviceType.GetMethod(nameof(IApprovalService.WithdrawAsync)));
    }
}
