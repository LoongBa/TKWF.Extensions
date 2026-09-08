using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批查询门面——实例分页/待办分页/详情含任务链。
/// <para>列表 DTO 剔除 BusinessDataJson 大字段；详情 GetInstanceDetailAsync 取全量。</para>
/// </summary>
public interface IApprovalQueryService
{
    /// <summary>审批实例分页查询（BusinessType/BusinessId/Status/Submitter/时间过滤 + Skip/Take）。</summary>
    Task<ApprovalInstancePagedResult> GetInstancesAsync(ApprovalInstanceQueryInput input, CancellationToken ct = default);

    /// <summary>待办任务分页查询（按 ApproverUserId + Status 过滤）。</summary>
    Task<ApprovalTaskPagedResult> GetPendingTasksAsync(ApprovalTaskQueryInput input, CancellationToken ct = default);

    /// <summary>审批实例详情（含任务链 + BusinessDataJson 全量）。</summary>
    Task<ApprovalInstanceDetailDto?> GetInstanceDetailAsync(long instanceId, CancellationToken ct = default);
}
