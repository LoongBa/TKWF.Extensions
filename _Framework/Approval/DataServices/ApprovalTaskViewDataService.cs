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
/// 审批任务-实例视图只读 DataService（VEntity V0.3.0）——跨表 JOIN 单查询下推 DB。
/// <para>VEntity 由 xCodeGen 跳过 DataService 模板（Engine.cs L42-46），此处手写继承
/// <see cref="DomainReadOnlyDataServiceBase{TEntity, TDto}"/>（2 参数版，与扩展现有 DataService 一致）。
/// 注入 <see cref="IEntityReadOnlyDAC{TEntity}"/>（只读契约）——绝不用 IEntityDAC（FreeSqlEntityDAC 静态守卫）。
/// 不标 <c>[GenerateController(FromDataService = true)]</c>：任务链查询是 GetInstanceDetailAsync 内部能力，
/// REST 经 IApprovalQueryService 门面暴露；ExposeGraphqlQuery=false（C4 敏感视图），无 GraphQL resolver。</para>
/// </summary>
partial class ApprovalTaskViewDataService(IDomainUser user, IEntityReadOnlyDAC<ApprovalTaskView> dac)
    : DomainReadOnlyDataServiceBase<ApprovalTaskView, ApprovalTaskViewDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>
    /// 单查询跨表：按 InstanceId 返回任务链（JOIN ApprovalTask → ApprovalInstance 下推 DB，携带 Instance 列，
    /// 替代 GetInstanceDetailAsync 任务链两步查询），StepIndex 升序 + Id 次键稳定排序。
    /// <para>limit 显式 <c>int.MaxValue</c>（无界——<c>MaxListLimit=1000</c> 保护，任务链查询语义为全量）。</para>
    /// </summary>
    public async Task<List<ApprovalTaskView>> GetTasksByInstanceIdAsync(long instanceId, CancellationToken ct = default)
        => await SelectAsync(v => v, predicate: v => v.InstanceId == instanceId,
            orderBy: q => q.OrderBy(v => v.StepIndex).ThenBy(v => v.Id), limit: int.MaxValue, ct: ct);
}