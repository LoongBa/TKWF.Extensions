using System;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.Approval;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// VEntity 集成测试（V0.3.0）——vw_ApprovalTaskView 跨表 JOIN 下推。
/// <para>N1: 按 InstanceId 返回任务链，StepIndex 升序（Join 下推 + 排序）；
/// N2: 视图行含 Instance 列（BusinessType/BusinessId/InstanceStatus 等读模型联邦透传）。</para>
/// </summary>
public class ApprovalVEntityTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly ApprovalTestHost _host;

    public ApprovalVEntityTests()
    {
        _fsql = ApprovalTestSupport.CreateInMemoryFreeSql();
        ApprovalTestSupport.SyncStructure(_fsql); // 含建 vw_ApprovalTaskView 真实视图
        _host = ApprovalTestSupport.Build(_fsql);
    }

    public void Dispose()
    {
        _host.Dispose();
        _fsql.Dispose();
    }

    // ── N1: Join 下推 + StepIndex 排序 ──

    [Fact]
    public async Task N1_VEntityView_GetTasksByInstanceId_ShouldReturnAscendingChain()
    {
        // 建 2 步流程 + 实例 + 提交（首步 1 任务）→ 推进首步 → 第 2 步任务生成（任务链 2 条）
        var (instanceId, firstTaskId) = await CreateTwoStepPendingInstanceAsync();
        await _host.ApprovalService.ApproveAsync(firstTaskId, "mgr");

        var tasks = await _host.TaskViewDataService.GetTasksByInstanceIdAsync(instanceId);

        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.Equal(instanceId, t.InstanceId));
        // StepIndex 升序（视图查询排序下推）
        Assert.True(tasks[0].StepIndex < tasks[1].StepIndex, $"StepIndex 未升序: {tasks[0].StepIndex}, {tasks[1].StepIndex}");
        Assert.Equal(0, tasks[0].StepIndex);
        Assert.Equal(1, tasks[1].StepIndex);
        Assert.Equal("经理", tasks[0].StepName);
        Assert.Equal("财务", tasks[1].StepName);
    }

    // ── N2: 读模型联邦——Instance 列透传 ──

    [Fact]
    public async Task N2_VEntityView_RowsShouldContainInstanceColumns()
    {
        var (instanceId, firstTaskId) = await CreateTwoStepPendingInstanceAsync();
        await _host.ApprovalService.ApproveAsync(firstTaskId, "mgr");

        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId);
        var rows = await _host.TaskViewDataService.GetTasksByInstanceIdAsync(instanceId);

        Assert.NotNull(instance);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            // Instance 列联邦透传（BusinessType/BusinessId/InstanceStatus/IsActive/CurrentStepIndex/Submitter/FlowCode/InstanceCreateTime）
            Assert.Equal(instance!.BusinessType, r.BusinessType);
            Assert.Equal(instance.BusinessId, r.BusinessId);
            Assert.Equal(instance.Status, r.InstanceStatus);
            Assert.Equal(instance.IsActive, r.IsActive);
            Assert.Equal(instance.CurrentStepIndex, r.CurrentStepIndex);
            Assert.Equal(instance.Submitter, r.Submitter);
            Assert.Equal(instance.FlowCode, r.FlowCode);
            Assert.Equal(instance.CreateTime, r.InstanceCreateTime);
            // 任务列透传（TaskCreateTime 真实值 + StepName 非空）
            Assert.True(r.TaskCreateTime != default, "TaskCreateTime 应为真实值");
            Assert.False(string.IsNullOrEmpty(r.StepName));
        });
        // 状态差异可见：首步已 Approved、次步 Pending——视图行反映真实任务状态
        Assert.Equal(ApprovalTaskStatus.Approved, rows.Single(r => r.StepIndex == 0).Status);
        Assert.Equal(ApprovalTaskStatus.Pending, rows.Single(r => r.StepIndex == 1).Status);
    }

    // ── 辅助 ──

    /// <summary>建 2 步流程 + 启动实例 + 提交（Pending，仅首步任务），返回 (实例ID, 首步任务ID)。</summary>
    private async Task<(long InstanceId, long FirstTaskId)> CreateTwoStepPendingInstanceAsync()
    {
        var steps = new[]
        {
            new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr"),
            new ApprovalStepDefinition(1, "财务", ApprovalApproverType.User, "fin")
        };
        await _host.ApprovalService.CreateFlowAsync("ve_flow", "VEntity 流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Expense", "exp-view", "ve_flow", "u_submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);

        var first = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId && t.StepIndex == 0, 0, 10, q => q, default)).First();
        return (instanceId, first.Id);
    }
}