namespace TKWF.Ext.Approval;

/// <summary>
/// 任务超时事件——超时服务处理 Remind 动作时派发（消费方催办通知）。
/// <para>Transfer/Jump/Approve/Reject 动作的效果经任务/实例状态可观测（系统身份审计 ApprovedBy="system:timeout"），
/// 不额外派发本事件（v0.2.0 语义——方案 §4.4 仅 Remind 派发）。</para>
/// </summary>
/// <param name="TaskId">超时任务 ID。</param>
/// <param name="InstanceId">审批实例 ID。</param>
/// <param name="BusinessType">业务实体类型名（如 "Expense"）。</param>
/// <param name="BusinessId">业务实体 ID（如 "exp-001"）。</param>
/// <param name="Action">超时动作（v0.2.0 仅 Remind 派发事件）。</param>
/// <param name="TransferToUserId">Transfer 动作目标用户（非 Transfer 为 null）。</param>
public sealed record ApprovalTaskTimeoutEvent(
    long TaskId,
    long InstanceId,
    string BusinessType,
    string BusinessId,
    ApprovalTimeoutAction Action,
    string? TransferToUserId);
