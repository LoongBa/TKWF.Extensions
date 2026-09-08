namespace TKWF.Ext.Approval;

/// <summary>
/// 审批完成事件——UoW 提交后派发（C3），消费方经 [DomainEventHandler] + ILocalEventHandler&lt;T&gt; 触发业务动作。
/// <para>事件 handler 异常不回滚审批（D15 post-commit 语义）；业务动作失败由消费方重试/补偿。</para>
/// </summary>
/// <param name="InstanceId">审批实例 ID。</param>
/// <param name="BusinessType">业务实体类型名（如 "Expense"）。</param>
/// <param name="BusinessId">业务实体 ID（如 "exp-001"）。</param>
/// <param name="Result">审批结果（Approved/Rejected）。</param>
/// <param name="Reason">驳回原因（仅 Rejected 时可能有值）。</param>
public sealed record ApprovalCompletedEvent(
    long InstanceId,
    string BusinessType,
    string BusinessId,
    ApprovalInstanceStatus Result,
    string? Reason = null);
