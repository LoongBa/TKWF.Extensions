namespace TKWF.Ext.Approval;

/// <summary>
/// 审批实例状态——Draft（草稿）/ Pending（审批中）/ Approved（已通过，终态）/ Rejected（已驳回，终态）/ Withdrawn（已撤回，终态）。
/// <para>状态机显式迁移（PrintTemplates 范式）：Draft→Pending（提交）→Approved（全步骤通过）/Rejected（任一步骤驳回）；
/// Pending→Withdrawn（撤回）；Draft→Withdrawn（撤回草稿）。终态（Approved/Rejected/Withdrawn）不可再操作。</para>
/// </summary>
public enum ApprovalInstanceStatus
{
    /// <summary>草稿（未提交，可编辑/撤回/删除）</summary>
    Draft = 0,

    /// <summary>审批中（已提交，等待审批任务完成）</summary>
    Pending = 1,

    /// <summary>已通过（终态——IsActive=false 释放唯一约束，允许重新提交）</summary>
    Approved = 2,

    /// <summary>已驳回（终态——IsActive=false 释放唯一约束，允许重新提交）</summary>
    Rejected = 3,

    /// <summary>已撤回（终态——IsActive=false 释放唯一约束，允许重新提交）</summary>
    Withdrawn = 4
}

/// <summary>
/// 审批任务状态——Pending（待办）/ Approved（已通过）/ Rejected（已驳回）/ Transferred（已转交）/ Completed（会签完成——同步骤全部通过后其余任务自动标记）。
/// </summary>
public enum ApprovalTaskStatus
{
    /// <summary>待办（等待审批人操作）</summary>
    Pending = 0,

    /// <summary>已通过（审批人通过）</summary>
    Approved = 1,

    /// <summary>已驳回（审批人驳回——实例终态 Rejected）</summary>
    Rejected = 2,

    /// <summary>已转交（原任务审批人转交给其他人）</summary>
    Transferred = 3,

    /// <summary>会签完成（同步骤全部通过后其余 Pending 任务自动标记）</summary>
    Completed = 4
}

/// <summary>
/// 审批人类型——User（指定用户 ID）/ Role（角色名，由消费方注册 IApprovalAssigneeResolver 解析为用户列表）。
/// </summary>
public enum ApprovalApproverType
{
    /// <summary>指定用户（ApproverValue = 用户 ID，内置直接解析）</summary>
    User = 0,

    /// <summary>角色（ApproverValue = 角色名，消费方须注册自定义 IApprovalAssigneeResolver 解析）</summary>
    Role = 1
}

/// <summary>
/// 审批模式——Any（或签，任一通过即本步骤通过）/ All（会签，全部通过才本步骤通过）。
/// </summary>
public enum ApprovalMode
{
    /// <summary>或签（任一任务通过即步骤通过，其余任务自动 Completed）</summary>
    Any = 0,

    /// <summary>会签（全部任务通过才步骤通过，任一驳回即步骤驳回）</summary>
    All = 1
}
