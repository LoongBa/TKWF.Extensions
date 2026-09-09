using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Approval;
using TKWF.Ext.Approval.DTOs;

namespace TKWF.Ext.Approval;

/// <summary>
/// 数据服务：审批任务实体——SG1 生成基础 CRUD，本 partial 类编写业务查询方法。
/// </summary>
partial class ApprovalTaskEntityDataService(IDomainUser user, IEntityDAC<ApprovalTaskEntity> dac)
    : DomainDataServiceBase<ApprovalTaskEntity, ApprovalTaskEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>查询指定实例指定步骤的全部任务。</summary>
    public async Task<List<ApprovalTaskEntity>> GetByInstanceAndStepAsync(
        long instanceId, int stepIndex, CancellationToken ct = default)
        => await EntitySelectAsync(
            t => t.InstanceId == instanceId && t.StepIndex == stepIndex,
            0, int.MaxValue, q => q.OrderBy(t => t.Id), ct);

    /// <summary>
    /// 抢占超时处理权（P3 扫描即占位）——仅当任务仍 Pending 且未处理时置 TimeoutProcessed=true。
    /// <para>返回 1=本调用独占处理权（可执行动作）；0=他者已处理/任务已非 Pending（跳过）。
    /// 先读后写（条件 EntityGetAsync + 列更新）——轻量引擎定位（P6 推荐③：re-fetch 复查足够），
    /// 唯一约束 + 事务已防主要竞态；双后台实例并发时以 TimeoutProcessed 状态过滤兜底。</para>
    /// </summary>
    public async Task<int> ClaimTimeoutAsync(long id, CancellationToken ct = default)
    {
        var pending = await EntityGetAsync(t =>
            t.Id == id && t.Status == ApprovalTaskStatus.Pending && !t.TimeoutProcessed, ct);
        if (pending == null) return 0;

        pending.TimeoutProcessed = true;
        await EntityUpdateColumnsBatchAsync(new[] { pending }, t => new { t.TimeoutProcessed }, ct);
        return 1;
    }
}
