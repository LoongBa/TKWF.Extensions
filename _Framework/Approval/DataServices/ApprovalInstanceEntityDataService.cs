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
/// 数据服务：审批实例实体——SG1 生成基础 CRUD，本 partial 类编写业务查询方法。
/// </summary>
partial class ApprovalInstanceEntityDataService(IDomainUser user, IEntityDAC<ApprovalInstanceEntity> dac)
    : DomainDataServiceBase<ApprovalInstanceEntity, ApprovalInstanceEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>查询同业务活动实例（C2 防重复检查）。</summary>
    public async Task<ApprovalInstanceEntity?> GetActiveByBusinessAsync(
        string businessType, string businessId, CancellationToken ct = default)
        => await EntityGetAsync(
            i => i.BusinessType == businessType && i.BusinessId == businessId && i.IsActive, ct);
}
