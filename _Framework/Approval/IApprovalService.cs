using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批引擎门面——流程定义管理 + 实例生命周期 + 审批/驳回/转交/撤回 + 事件派发。
/// <para>Scoped 生命周期（按请求）。注入 SG1 DataService 委托 + ITransactionManager + ILocalEventBus + IApprovalAssigneeResolver。</para>
/// </summary>
public interface IApprovalService
{
    // ── 流程定义 ──

    /// <summary>创建审批流定义（Code 唯一——唯一索引 UX_ApprovalFlow_Code 并发冲突显式异常）。</summary>
    Task<long> CreateFlowAsync(string code, string name, IReadOnlyList<ApprovalStepDefinition> steps,
        string? description = null, CancellationToken ct = default);

    /// <summary>更新审批流定义（名称/步骤/描述）。</summary>
    Task UpdateFlowAsync(long flowId, string name, IReadOnlyList<ApprovalStepDefinition> steps,
        string? description = null, CancellationToken ct = default);

    /// <summary>启用审批流。</summary>
    Task EnableFlowAsync(long flowId, CancellationToken ct = default);

    /// <summary>禁用审批流（禁用后 StartAsync 抛异常）。</summary>
    Task DisableFlowAsync(long flowId, CancellationToken ct = default);

    // ── 实例 ──

    /// <summary>
    /// 启动审批实例（Draft + IsActive=true）。
    /// <para>前置校验：flow.IsEnabled（P1-3）+ 同业务无活动实例（C2 前置检查）。
    /// C4 评审：ccUserIds 追加于 ct 之后（方案 A）——现有位置传参调用零影响；传抄送人须用命名参数 ccUserIds:。</para>
    /// </summary>
    Task<long> StartAsync(string businessType, string businessId, string flowCode, string submitter,
        string? businessDataJson = null, CancellationToken ct = default, string[]? ccUserIds = null);

    /// <summary>
    /// 提交审批（Draft→Pending + 创建首步审批任务）。
    /// <para>v0.2.0：提交成功后派发 Start 位置抄送事件（ApprovalCcNotifiedEvent，post-commit）。</para>
    /// </summary>
    Task SubmitAsync(long instanceId, CancellationToken ct = default);

    // ── 任务 ──

    /// <summary>
    /// 审批通过（C1 身份硬校验 + 任务通过→步骤判定→下步/完成 + 终态释放 IsActive）。
    /// <para>v0.2.0：委派中（DelegationState==Pending）拒绝 Complete——委派人只可 Resolve（Flowable 语义）。</para>
    /// </summary>
    Task ApproveAsync(long taskId, string approverUserId, string? comment = null, CancellationToken ct = default);

    /// <summary>
    /// 驳回（C1 身份硬校验 + 实例 Rejected 终态 + IsActive=false 释放约束）。
    /// </summary>
    Task RejectAsync(long taskId, string approverUserId, string reason, CancellationToken ct = default);

    /// <summary>
    /// 转交（C1/C4 仅任务审批人可转交 + 原任务 Transferred + 新任务 Pending）。
    /// </summary>
    Task TransferAsync(long taskId, string fromUserId, string toUserId, CancellationToken ct = default);

    /// <summary>
    /// 撤回（P1-1 仅提交人 + Draft/Pending→Withdrawn 终态 + IsActive=false）。
    /// <para>P2-7（P9 评审）：v0.2.0 补齐事务包裹（实例更新 + 任务 Completed 原子提交）。</para>
    /// </summary>
    Task WithdrawAsync(long instanceId, string userId, CancellationToken ct = default);

    // ── v0.2.0：委派（Flowable 两阶段）──

    /// <summary>
    /// 委派审批任务（fromUserId=当前审批人 + DelegationState==None + Pending）。
    /// <para>同任务状态切换：ApproverUserId=toUserId + DelegationState=Pending + OriginalAssigneeId=fromUserId
    /// （与 TransferAsync 的区别：Transfer 创建新任务原任务 Transferred；Delegate 同任务状态切换）。
    /// 委派单层不嵌套（P1）：DelegationState==Pending 拒绝再委派；Resolve 后不回退 None。</para>
    /// </summary>
    Task DelegateTaskAsync(long taskId, string fromUserId, string toUserId, CancellationToken ct = default);

    /// <summary>
    /// 解决委派（delegateUserId=委派人 + DelegationState==Pending → Resolved + ApproverUserId 回 OriginalAssigneeId）。
    /// <para>委派人不可直接 Complete（ApproveAsync 拒绝 Pending 委派）；须 Resolve 后由原审批人复核 Complete。</para>
    /// </summary>
    Task ResolveTaskAsync(long taskId, string delegateUserId, string? comment = null, CancellationToken ct = default);

    // ── v0.2.0：加签（钉钉步骤内模型）──

    /// <summary>
    /// 当前审批人动态追加审批人（运行时步骤内并行追加）。
    /// <para>校验：任务 Pending + operatedByUserId==ApproverUserId（C1）。去重（C2）：appenderUserIds 自身 Distinct
    /// + 与同步骤 Pending 任务审批人集合交集非空抛异常。Participate=建新任务（加签人待办，超时继承步骤配置 P4）；
    /// Notify=仅记录 + 发布 <see cref="ApprovalAppendNotifyEvent"/>（post-commit，C3）。</para>
    /// </summary>
    Task AppendApproverAsync(long taskId, string operatedByUserId, string[] appenderUserIds,
        ApprovalAppendMode mode = ApprovalAppendMode.Participate,
        string? remark = null, CancellationToken ct = default);

    // ── v0.2.0：抄送（独立实体 + 事件通知）──

    /// <summary>
    /// 运行中追加抄送人（记录 ApprovalCCEntity(Position=Finish)——实例终态时通知）。
    /// <para>终态实例（Approved/Rejected/Withdrawn）不可追加。</para>
    /// </summary>
    Task AddCCAsync(long instanceId, string[] userIds, CancellationToken ct = default);
}
