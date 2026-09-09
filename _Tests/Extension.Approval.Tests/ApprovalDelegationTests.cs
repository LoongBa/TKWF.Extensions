using System;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.Approval;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// v0.2.0 委派测试——D1-D3（Flowable 两阶段模型：委派 → 解决 → 原审批人复核）。
/// <para>D1: DelegateTaskAsync 后任务 ApproverUserId=toUserId + DelegationState=Pending + OriginalAssigneeId 记录
/// D2: ResolveTaskAsync 后 ApproverUserId 回原审批人 + DelegationState=Resolved
/// D3: 委派中（Pending）委派人不可 Complete（须先 Resolve）；非 fromUserId 不可委派；委派中不可再委派（嵌套阻断，P1）</para>
/// </summary>
public class ApprovalDelegationTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly ApprovalTestHost _host;

    public ApprovalDelegationTests()
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

    /// <summary>建单步骤流程并提交，返回任务。</summary>
    private async Task<ApprovalTaskEntity> StartSubmittedTaskAsync(string flowCode = "delegate_flow")
    {
        var steps = new[] { new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "owner") };
        await _host.ApprovalService.CreateFlowAsync(flowCode, "流程", steps);
        var instanceId = await _host.ApprovalService.StartAsync("Biz", Guid.NewGuid().ToString("N"), flowCode, "submitter");
        await _host.ApprovalService.SubmitAsync(instanceId);
        return (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, default)).First();
    }

    [Fact]
    public async Task D1_DelegateTaskAsync_ShouldSetDelegationFields()
    {
        var task = await StartSubmittedTaskAsync();

        await _host.ApprovalService.DelegateTaskAsync(task.Id, "owner", "deputy");

        var updated = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalDelegationState.Pending, updated!.DelegationState);
        Assert.Equal("owner", updated.OriginalAssigneeId);
        Assert.Equal("deputy", updated.DelegatedToUserId);
        Assert.Equal("deputy", updated.ApproverUserId); // 委派人成为当前处理人
        Assert.True(updated.DelegatedAt.HasValue);
        Assert.Equal(ApprovalTaskStatus.Pending, updated.Status); // 同任务状态切换（不建新任务）
    }

    [Fact]
    public async Task D1_DelegateTaskAsync_NonOwner_ShouldThrow()
    {
        var task = await StartSubmittedTaskAsync();

        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.DelegateTaskAsync(task.Id, "wrong_user", "deputy"));
    }

    [Fact]
    public async Task D2_ResolveTaskAsync_ShouldReturnToOriginalAssignee()
    {
        var task = await StartSubmittedTaskAsync();
        await _host.ApprovalService.DelegateTaskAsync(task.Id, "owner", "deputy");

        await _host.ApprovalService.ResolveTaskAsync(task.Id, "deputy", "已核实，请复核");

        var updated = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalDelegationState.Resolved, updated!.DelegationState);
        Assert.Equal("owner", updated.ApproverUserId); // 回到原审批人
        Assert.Equal("已核实，请复核", updated.Comment);
    }

    [Fact]
    public async Task D2_ResolveTaskAsync_NonDelegate_ShouldThrow()
    {
        var task = await StartSubmittedTaskAsync();
        await _host.ApprovalService.DelegateTaskAsync(task.Id, "owner", "deputy");

        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.ResolveTaskAsync(task.Id, "wrong_user"));
    }

    [Fact]
    public async Task D3_DelegatePending_DelegateCannotComplete_ShouldThrow()
    {
        var task = await StartSubmittedTaskAsync();
        await _host.ApprovalService.DelegateTaskAsync(task.Id, "owner", "deputy");

        // 委派中（Pending）委派人不可 Complete（须先 Resolve——Flowable 语义）
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.ApproveAsync(task.Id, "deputy"));
    }

    [Fact]
    public async Task D3_AfterResolve_OriginalAssigneeCanApprove()
    {
        var task = await StartSubmittedTaskAsync();
        await _host.ApprovalService.DelegateTaskAsync(task.Id, "owner", "deputy");
        await _host.ApprovalService.ResolveTaskAsync(task.Id, "deputy");

        // 原审批人复核后 Complete——实例 Approved
        await _host.ApprovalService.ApproveAsync(task.Id, "owner", "同意");

        var updated = await _host.TaskDataService.EntityGetAsync(t => t.Id == task.Id);
        Assert.Equal(ApprovalTaskStatus.Approved, updated!.Status);
        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == task.InstanceId);
        Assert.Equal(ApprovalInstanceStatus.Approved, instance!.Status);
    }

    [Fact]
    public async Task D3_DelegatePending_DelegateCannotDelegateAgain_ShouldThrow()
    {
        var task = await StartSubmittedTaskAsync();
        await _host.ApprovalService.DelegateTaskAsync(task.Id, "owner", "deputy");

        // P1 嵌套阻断：委派中（Pending）再委派（deputy 是当前处理人，身份校验通过但嵌套阻断）→ 抛
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.DelegateTaskAsync(task.Id, "deputy", "third"));
    }

    [Fact]
    public async Task D3_AfterResolve_CannotDelegateAgain_ShouldThrow()
    {
        var task = await StartSubmittedTaskAsync();
        await _host.ApprovalService.DelegateTaskAsync(task.Id, "owner", "deputy");
        await _host.ApprovalService.ResolveTaskAsync(task.Id, "deputy");

        // P1：Resolve 后不回退 None（Resolved）——原审批人不可再次委派同任务（须用 Transfer 替代）
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.DelegateTaskAsync(task.Id, "owner", "deputy"));
    }

    [Fact]
    public async Task D1_DelegateTaskAsync_SelfDelegation_ShouldThrow()
    {
        var task = await StartSubmittedTaskAsync();

        // 防御守卫（实施偏离 #5）：委托给自己（fromUserId == toUserId）→ 拒绝
        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.DelegateTaskAsync(task.Id, "owner", "owner"));
    }
}
