using System.Collections.Generic;

namespace TKWF.Ext.Approval;

/// <summary>
/// 加签通知事件（Notify 模式）——post-commit 派发（C3 范式），消费方组装通知投递。
/// <para>Participate 模式不派发此事件（加签人获得真实审批任务待办）；
/// Notify 模式仅记录 + 派发事件，通知对象经 <see cref="NotifyUserIds"/> 给出。</para>
/// </summary>
/// <param name="InstanceId">审批实例 ID。</param>
/// <param name="TaskId">触发加签的任务 ID。</param>
/// <param name="StepIndex">步骤序号。</param>
/// <param name="NotifyUserIds">被通知用户 ID 列表（加签人，已去重）。</param>
/// <param name="OperatedByUserId">操作人（当前审批人）。</param>
/// <param name="Remark">加签理由（可选）。</param>
public sealed record ApprovalAppendNotifyEvent(
    long InstanceId,
    long TaskId,
    int StepIndex,
    IReadOnlyList<string> NotifyUserIds,
    string OperatedByUserId,
    string? Remark);
