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
    /// <para>前置校验：flow.IsEnabled（P1-3）+ 同业务无活动实例（C2 前置检查）。</para>
    /// </summary>
    Task<long> StartAsync(string businessType, string businessId, string flowCode, string submitter,
        string? businessDataJson = null, CancellationToken ct = default);

    /// <summary>
    /// 提交审批（Draft→Pending + 创建首步审批任务）。
    /// </summary>
    Task SubmitAsync(long instanceId, CancellationToken ct = default);

    // ── 任务 ──

    /// <summary>
    /// 审批通过（C1 身份硬校验 + 任务通过→步骤判定→下步/完成 + 终态释放 IsActive）。
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
    /// </summary>
    Task WithdrawAsync(long instanceId, string userId, CancellationToken ct = default);
}
