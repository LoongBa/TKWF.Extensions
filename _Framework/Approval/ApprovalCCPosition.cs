namespace TKWF.Ext.Approval;

/// <summary>
/// 抄送位置——Start（发起时通知）/ Finish（完成时通知）/ StartFinish（两者都通知）。
/// <para>实例终态（Approved/Rejected）时通知 Finish/StartFinish 位置抄送人；
/// Start/StartFinish 位置抄送人于实例提交（SubmitAsync）后通知。
/// Withdrawn 不触发 Finish 通知（P7 评审：撤回=主动取消非审批结果）。</para>
/// </summary>
public enum ApprovalCCPosition
{
    /// <summary>发起时通知（StartAsync 记录 + SubmitAsync 后派发事件）。</summary>
    Start = 0,

    /// <summary>完成时通知（实例 Approved/Rejected 终态派发事件）。</summary>
    Finish = 1,

    /// <summary>发起与完成均通知。</summary>
    StartFinish = 2
}
