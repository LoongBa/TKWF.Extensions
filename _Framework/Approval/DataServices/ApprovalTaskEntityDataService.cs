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
            0, 1000, q => q.OrderBy(t => t.Id), ct);
}
