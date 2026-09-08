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
/// 数据服务：审批流定义实体——SG1 生成基础 CRUD，本 partial 类编写业务查询方法。
/// </summary>
partial class ApprovalFlowEntityDataService(IDomainUser user, IEntityDAC<ApprovalFlowEntity> dac)
    : DomainDataServiceBase<ApprovalFlowEntity, ApprovalFlowEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>按流程编码查询。</summary>
    public async Task<ApprovalFlowEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await EntityGetAsync(f => f.Code == code, ct);
}
