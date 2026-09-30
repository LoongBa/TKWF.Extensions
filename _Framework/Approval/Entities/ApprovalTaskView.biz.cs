using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>审批任务-实例视图实体（VEntity）——跨表 JOIN <c>ApprovalTask</c> → <c>ApprovalInstance</c>，  按 InstanceId 单查询返回任务链 + Instance 列（读模型联邦）。  <para>V0.3.0：替代 <c>GetInstanceDetailAsync</c> 任务链两步查询（先查任务再查实例列）——JOIN 下推 DB，单查询完成。  VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。  ⚠️ 敏感视图（含审批人/审批意见列，C4）：ExposeGraphqlQuery = false，数据访问统一经 IApprovalQueryService 门面。</para></summary>
public partial class ApprovalTaskView
{
    /// <summary>
    /// 根据需要添加业务验证逻辑 (例如跨表验证、状态机检查) 
    /// </summary>
    partial void OnBusinessValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：领域驱动设计的业务验证规则 
        // if (this.Status == Status.Disabled && this.Stock > 0)
        //     results.Add(new ValidationResult("禁用状态下不能有库存", new[] { nameof(Status) }));
    }
}