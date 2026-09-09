using System.Collections.Generic;

namespace TKWF.Ext.Approval;

/// <summary>
/// 抄送通知事件——post-commit 派发（C3 范式），消费方组装 Notifications 投递（本扩展只发事件不投递）。
/// <para>Start 位置事件于实例提交（SubmitAsync）后派发（Result=null——尚未有审批结果）；
/// Finish 位置事件于实例 Approved/Rejected 终态派发（Result 强类型，P11）。
/// Withdrawn 不派发（P7 评审：撤回=主动取消非审批结果）。</para>
/// </summary>
/// <param name="InstanceId">审批实例 ID。</param>
/// <param name="BusinessType">业务实体类型名（如 "Expense"）。</param>
/// <param name="BusinessId">业务实体 ID（如 "exp-001"）。</param>
/// <param name="Result">审批结果（Finish 通知为 Approved/Rejected；Start 通知为 null）。</param>
/// <param name="Position">抄送位置（Start=发起通知 / Finish=完成通知）。</param>
/// <param name="CcUserIds">抄送人用户 ID 列表（已去重）。</param>
public sealed record ApprovalCcNotifiedEvent(
    long InstanceId,
    string BusinessType,
    string BusinessId,
    ApprovalInstanceStatus? Result,
    ApprovalCCPosition Position,
    IReadOnlyList<string> CcUserIds);
