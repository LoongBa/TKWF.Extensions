using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Events;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批超时处理服务实现——经 SG1 DataService 委托查询/占位 + ApprovalManager internal 系统动作方法。
/// <para>数据访问红线：不注入 IFreeSql/IEntityDAC——扫描/占位走 <see cref="ApprovalTaskEntityDataService"/>（条件查询 + ClaimTimeoutAsync），
/// 动作走 <see cref="ApprovalManager"/> internal 系统方法（同程序集访问）。</para>
/// </summary>
internal sealed class ApprovalTimeoutService(
    ApprovalTaskEntityDataService taskDataService,
    ApprovalInstanceEntityDataService instanceDataService,
    ApprovalManager approvalManager,
    ILocalEventBus localEventBus,
    ILogger<ApprovalTimeoutService> logger) : IApprovalTimeoutService
{
    /// <inheritdoc />
    public async Task<int> ProcessTimeoutTasksAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // 扫描超期任务（Pending && TimeoutAt<=now && !TimeoutProcessed）
        var overdue = await taskDataService.EntitySelectAsync(
            t => t.Status == ApprovalTaskStatus.Pending
                 && t.TimeoutAt <= now
                 && !t.TimeoutProcessed,
            0, int.MaxValue, q => q.OrderBy(t => t.Id), ct);

        var processed = 0;
        foreach (var task in overdue)
        {
            ct.ThrowIfCancellationRequested();

            // P3 扫描即占位——条件更新影响行数=0 跳过（他者已处理 / 任务已非 Pending）
            var claimed = await taskDataService.ClaimTimeoutAsync(task.Id, ct);
            if (claimed == 0)
            {
                logger.LogDebug("超时任务 {TaskId} 已被他者处理，跳过", task.Id);
                continue;
            }
            processed++;

            // 按步骤动作快照执行
            var action = task.TimeoutAction ?? ApprovalTimeoutAction.Remind;
            try
            {
                await ExecuteActionAsync(task, action, ct);
            }
            catch (Exception ex)
            {
                // 动作失败不中断整批扫描——记录日志（占位已生效，避免重复执行）
                logger.LogError(ex, "超时任务 {TaskId} 动作 {Action} 执行失败", task.Id, action);
            }
        }

        return processed;
    }

    private async Task ExecuteActionAsync(ApprovalTaskEntity task, ApprovalTimeoutAction action, CancellationToken ct)
    {
        switch (action)
        {
            case ApprovalTimeoutAction.None:
                // 仅标记 TimeoutProcessed（占位已做）——不处理不误操作
                logger.LogInformation("超时任务 {TaskId} 动作 None，仅标记处理", task.Id);
                break;

            case ApprovalTimeoutAction.Remind:
                await PublishTimeoutEventAsync(task, ApprovalTimeoutAction.Remind, null, ct);
                break;

            case ApprovalTimeoutAction.Transfer:
                if (string.IsNullOrWhiteSpace(task.TimeoutTransferToUserId))
                    throw new InvalidApprovalOperationException($"任务 {task.Id} 超时 Transfer 未配置 TimeoutTransferToUserId");
                await approvalManager.TransferAsSystemAsync(task.Id, task.TimeoutTransferToUserId, ct);
                break;

            case ApprovalTimeoutAction.Jump:
                await approvalManager.JumpAsSystemAsync(task.Id, ct);
                break;

            case ApprovalTimeoutAction.Approve:
                await approvalManager.ApproveAsSystemAsync(task.Id, null, ct);
                break;

            case ApprovalTimeoutAction.Reject:
                await approvalManager.RejectAsSystemAsync(task.Id, "审批超时自动驳回", ct);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), $"未知超时动作: {action}");
        }
    }

    /// <summary>发布超时事件（Remind 动作——消费方催办通知）。</summary>
    private async Task PublishTimeoutEventAsync(ApprovalTaskEntity task, ApprovalTimeoutAction action,
        string? transferToUserId, CancellationToken ct)
    {
        var instance = await instanceDataService.EntityGetAsync(i => i.Id == task.InstanceId, ct);
        if (instance == null)
        {
            logger.LogWarning("超时任务 {TaskId} 关联实例 {InstanceId} 不存在，跳过事件", task.Id, task.InstanceId);
            return;
        }

        var evt = new ApprovalTaskTimeoutEvent(
            task.Id, instance.Id, instance.BusinessType, instance.BusinessId, action, transferToUserId);
        try
        {
            await localEventBus.PublishAsync(evt);
            logger.LogInformation("超时提醒事件已派发: TaskId={TaskId}, InstanceId={InstanceId}", task.Id, instance.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "超时提醒事件派发失败（不影响超时处理）: TaskId={TaskId}", task.Id);
        }
    }
}
