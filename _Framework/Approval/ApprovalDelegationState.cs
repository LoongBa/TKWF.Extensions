namespace TKWF.Ext.Approval;

/// <summary>
/// 审批任务委派状态（Flowable 两阶段模型：委派 → 解决 → 原审批人复核）。
/// <para>v0.2.0 新增（v0.1.0 任务无委派概念）——委派单层不嵌套（P1 评审）：
/// None（无委派）→ Pending（委派中，委派人为当前处理人）→ Resolved（已解决，任务回到原审批人）。
/// Resolve 后不回退 None——原审批人复核后须用 Transfer 替代再委派。</para>
/// </summary>
public enum ApprovalDelegationState
{
    /// <summary>无委派（默认——普通任务）。</summary>
    None = 0,

    /// <summary>委派中（委派人为当前处理人，不可 Complete 只可 Resolve）。</summary>
    Pending = 1,

    /// <summary>已解决（任务回到原审批人 ApproverUserId=OriginalAssigneeId，仅原审批人可 Complete）。</summary>
    Resolved = 2
}
