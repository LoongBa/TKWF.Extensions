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
/// 数据服务：抄送记录实体——SG1 生成基础 CRUD，本 partial 类编写业务查询方法。
/// </summary>
partial class ApprovalCCEntityDataService(IDomainUser user, IEntityDAC<ApprovalCCEntity> dac)
    : DomainDataServiceBase<ApprovalCCEntity, ApprovalCCEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>查询指定实例的全部抄送记录。</summary>
    public async Task<System.Collections.Generic.List<ApprovalCCEntity>> GetByInstanceAsync(
        long instanceId, CancellationToken ct = default)
        => await EntitySelectAsync(
            c => c.InstanceId == instanceId,
            0, 1000, q => q.OrderBy(c => c.Id), ct);
}
