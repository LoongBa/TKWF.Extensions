using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Approval;
using TKWF.Ext.Approval.DTOs;

namespace TKWF.Ext.Approval;

/// <summary>
/// 数据服务：加签记录实体——SG1 生成基础 CRUD，本 partial 类编写业务查询方法。
/// </summary>
partial class ApprovalAppendEntityDataService(IDomainUser user, IEntityDAC<ApprovalAppendEntity> dac)
    : DomainDataServiceBase<ApprovalAppendEntity, ApprovalAppendEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>查询指定实例的全部加签记录。</summary>
    public async Task<System.Collections.Generic.List<ApprovalAppendEntity>> GetByInstanceAsync(
        long instanceId, CancellationToken ct = default)
        => await EntitySelectAsync(
            a => a.InstanceId == instanceId,
            0, 1000, q => q.OrderBy(a => a.Id), ct);
}
