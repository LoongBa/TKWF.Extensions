using System;
using System.Linq;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.Approval;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// v0.2.0 抄送测试——D7-D8（独立实体 + 事件通知，投递组装消费方 Notifications）。
/// <para>D7: Start 带 ccUserIds（命名参数，C4）记录 Start 位置 + Finish 终态事件 + 运行中 AddCC
/// D8: ApprovalCcNotifiedEvent 含 InstanceId/BusinessType/BusinessId/Result（强类型 P11）/Position/CcUserIds；
///     Withdrawn 不触发 Finish（P7）</para>
/// </summary>
public class ApprovalCCTests : IDisposable
{
    private readonly IFreeSql _fsql;
    private readonly ApprovalTestHost _host;

    public ApprovalCCTests()
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

    private async Task CreateFlowAsync(string code)
    {
        var steps = new[] { new ApprovalStepDefinition(0, "经理", ApprovalApproverType.User, "mgr") };
        await _host.ApprovalService.CreateFlowAsync(code, "流程", steps);
    }

    [Fact]
    public async Task D7_StartAsync_WithCcUserIds_NamedParam_ShouldRecordStartCC()
    {
        // C4：ccUserIds 用命名参数（追加于 ct 之后）——调用成功即参数位置正确
        await CreateFlowAsync("cc_start");
        var instanceId = await _host.ApprovalService.StartAsync(
            "Biz", Guid.NewGuid().ToString("N"), "cc_start", "submitter", ccUserIds: new[] { "manager1", "manager2" },
            ct: TestContext.Current.CancellationToken);

        var ccs = await _host.CcDataService.GetByInstanceAsync(instanceId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, ccs.Count);
        Assert.All(ccs, c => Assert.Equal(ApprovalCCPosition.Start, c.Position));
        Assert.Equal(new[] { "manager1", "manager2" }, ccs.Select(c => c.UserId).OrderBy(u => u).ToArray());
    }

    [Fact]
    public async Task D7_Submit_ShouldPublishStartCCEvent()
    {
        await CreateFlowAsync("cc_submit");
        var instanceId = await _host.ApprovalService.StartAsync(
            "Biz", Guid.NewGuid().ToString("N"), "cc_submit", "submitter", ccUserIds: new[] { "notify_user" },
            ct: TestContext.Current.CancellationToken);
        _host.Events.PublishedEvents.Clear();

        await _host.ApprovalService.SubmitAsync(instanceId, ct: TestContext.Current.CancellationToken);

        // Start 位置事件：Result=null（尚未有审批结果）
        var evt = _host.Events.PublishedEvents.OfType<ApprovalCcNotifiedEvent>().FirstOrDefault();
        Assert.NotNull(evt);
        Assert.Equal(instanceId, evt!.InstanceId);
        Assert.Equal(ApprovalCCPosition.Start, evt.Position);
        Assert.Null(evt.Result);
        Assert.Equal(new[] { "notify_user" }, evt.CcUserIds);
    }

    [Fact]
    public async Task D7_Finish_ShouldPublishFinishCCEvent()
    {
        await CreateFlowAsync("cc_finish");
        var instanceId = await _host.ApprovalService.StartAsync(
            "Biz", Guid.NewGuid().ToString("N"), "cc_finish", "submitter", ccUserIds: new[] { "notify_user" },
            ct: TestContext.Current.CancellationToken);
        await _host.ApprovalService.SubmitAsync(instanceId, ct: TestContext.Current.CancellationToken);

        // 运行中追加 Finish 位置抄送人（终态通知对象——Start 位置抄送人仅收 Start 事件）
        await _host.ApprovalService.AddCCAsync(instanceId, new[] { "finance" }, ct: TestContext.Current.CancellationToken);
        _host.Events.PublishedEvents.Clear();

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, TestContext.Current.CancellationToken)).First();
        await _host.ApprovalService.ApproveAsync(task.Id, "mgr", ct: TestContext.Current.CancellationToken);

        // Finish 位置事件：Result=Approved（强类型 P11），CcUserIds=Finish 位置抄送人
        var evt = _host.Events.PublishedEvents.OfType<ApprovalCcNotifiedEvent>()
            .FirstOrDefault(e => e.Position == ApprovalCCPosition.Finish);
        Assert.NotNull(evt);
        Assert.Equal(instanceId, evt!.InstanceId);
        Assert.Equal("Biz", evt.BusinessType);
        Assert.Equal(ApprovalCCPosition.Finish, evt.Position);
        Assert.Equal(ApprovalInstanceStatus.Approved, evt.Result); // P11：强类型
        Assert.Equal(new[] { "finance" }, evt.CcUserIds);
    }

    [Fact]
    public async Task D7_AddCCAsync_Running_ShouldRecordFinishCC()
    {
        await CreateFlowAsync("cc_add");
        var instanceId = await _host.ApprovalService.StartAsync("Biz", Guid.NewGuid().ToString("N"), "cc_add", "submitter", ct: TestContext.Current.CancellationToken);
        await _host.ApprovalService.SubmitAsync(instanceId, ct: TestContext.Current.CancellationToken);

        // 运行中追加抄送
        await _host.ApprovalService.AddCCAsync(instanceId, new[] { "late_viewer" }, ct: TestContext.Current.CancellationToken);

        var ccs = await _host.CcDataService.GetByInstanceAsync(instanceId, ct: TestContext.Current.CancellationToken);
        Assert.Single(ccs);
        Assert.Equal("late_viewer", ccs[0].UserId);
        Assert.Equal(ApprovalCCPosition.Finish, ccs[0].Position); // 运行中追加→完成时通知
    }

    [Fact]
    public async Task D7_AddCCAsync_OnTerminalState_ShouldThrow()
    {
        await CreateFlowAsync("cc_add_terminal");
        var instanceId = await _host.ApprovalService.StartAsync("Biz", Guid.NewGuid().ToString("N"), "cc_add_terminal", "submitter", ct: TestContext.Current.CancellationToken);
        await _host.ApprovalService.SubmitAsync(instanceId, ct: TestContext.Current.CancellationToken);
        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, TestContext.Current.CancellationToken)).First();
        await _host.ApprovalService.ApproveAsync(task.Id, "mgr", ct: TestContext.Current.CancellationToken); // Approved 终态

        await Assert.ThrowsAsync<InvalidApprovalOperationException>(
            () => _host.ApprovalService.AddCCAsync(instanceId, new[] { "late_viewer" }, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task D8_Withdrawn_ShouldNotPublishFinishCC()
    {
        await CreateFlowAsync("cc_withdraw");
        var instanceId = await _host.ApprovalService.StartAsync(
            "Biz", Guid.NewGuid().ToString("N"), "cc_withdraw", "submitter", ccUserIds: new[] { "watcher" },
            ct: TestContext.Current.CancellationToken);
        await _host.ApprovalService.SubmitAsync(instanceId, ct: TestContext.Current.CancellationToken);
        _host.Events.PublishedEvents.Clear();

        await _host.ApprovalService.WithdrawAsync(instanceId, "submitter", ct: TestContext.Current.CancellationToken);

        // P7：Withdrawn 不触发 CC Finish 事件
        var instance = await _host.InstanceDataService.EntityGetAsync(i => i.Id == instanceId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalInstanceStatus.Withdrawn, instance!.Status);
        Assert.Empty(_host.Events.PublishedEvents.OfType<ApprovalCcNotifiedEvent>());
    }

    [Fact]
    public async Task D8_StartFinish_Position_ShouldNotifyBoth()
    {
        await CreateFlowAsync("cc_both");
        var instanceId = await _host.ApprovalService.StartAsync(
            "Biz", Guid.NewGuid().ToString("N"), "cc_both", "submitter", ccUserIds: new[] { "dual_viewer" },
            ct: TestContext.Current.CancellationToken);

        // 追加 StartFinish 位置记录（模拟组合位置抄送人）
        await _host.ApprovalService.AddCCAsync(instanceId, new[] { "dual_viewer" }, ct: TestContext.Current.CancellationToken);
        // 修正为 StartFinish 位置（AddCC 默认 Finish——直接改记录模拟 StartFinish 场景）
        var ccs = await _host.CcDataService.GetByInstanceAsync(instanceId, ct: TestContext.Current.CancellationToken);
        foreach (var cc in ccs.Where(c => c.UserId == "dual_viewer"))
        {
            cc.Position = ApprovalCCPosition.StartFinish;
            await _host.CcDataService.EntityUpdateAsync(cc, TestContext.Current.CancellationToken);
        }

        await _host.ApprovalService.SubmitAsync(instanceId, ct: TestContext.Current.CancellationToken);

        // Start 位置事件在提交后派发——断言后再清空（避免与 Finish 事件混淆）
        var startEvt = _host.Events.PublishedEvents.OfType<ApprovalCcNotifiedEvent>()
            .FirstOrDefault(e => e.Position == ApprovalCCPosition.Start);
        Assert.NotNull(startEvt);
        Assert.Contains("dual_viewer", startEvt!.CcUserIds);

        _host.Events.PublishedEvents.Clear();

        var task = (await _host.TaskDataService.EntitySelectAsync(
            t => t.InstanceId == instanceId, 0, 10, q => q, TestContext.Current.CancellationToken)).First();
        await _host.ApprovalService.ApproveAsync(task.Id, "mgr", ct: TestContext.Current.CancellationToken);

        // StartFinish 抄送人：Finish 亦收到通知（Start 已在提交后断言）
        var finishEvt = _host.Events.PublishedEvents.OfType<ApprovalCcNotifiedEvent>()
            .FirstOrDefault(e => e.Position == ApprovalCCPosition.Finish);
        Assert.NotNull(finishEvt);
        Assert.Contains("dual_viewer", finishEvt!.CcUserIds);
    }
}
