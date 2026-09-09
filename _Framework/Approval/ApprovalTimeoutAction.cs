namespace TKWF.Ext.Approval;

/// <summary>
/// 任务超时自动处理动作（钉钉 5 动作模型 + None）。
/// <para>步骤级配置于 <see cref="ApprovalStepDefinition.TimeoutAction"/>；任务创建时快照至
/// <see cref="ApprovalTaskEntity.TimeoutAction"/>。None=不处理（任务保持 Pending，仅置超时已处理防重扫）。</para>
/// </summary>
public enum ApprovalTimeoutAction
{
    /// <summary>不处理（仅标记 TimeoutProcessed=true——不重扫不误操作）。</summary>
    None = 0,

    /// <summary>提醒——发布 <see cref="ApprovalTaskTimeoutEvent"/>（消费方催办通知）。</summary>
    Remind = 1,

    /// <summary>转交——转交给 <see cref="ApprovalStepDefinition.TimeoutTransferToUserId"/>（系统身份，审计 TransferredTo=目标）。</summary>
    Transfer = 2,

    /// <summary>跳转——跳到 <see cref="ApprovalStepDefinition.TimeoutJumpToStepIndex"/>（须晚于当前步骤，越界/向后/等于当前拒绝）。</summary>
    Jump = 3,

    /// <summary>自动通过——系统身份批准（审计 ApprovedBy="system:timeout"）。</summary>
    Approve = 4,

    /// <summary>自动驳回——系统身份驳回（审计 ApprovedBy="system:timeout"）。</summary>
    Reject = 5
}
