using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Approval.DTOs;

/// <summary>审批任务-实例视图实体（VEntity）——跨表 JOIN <c>ApprovalTask</c> → <c>ApprovalInstance</c>，  按 InstanceId 单查询返回任务链 + Instance 列（读模型联邦）。  <para>V0.3.0：替代 <c>GetInstanceDetailAsync</c> 任务链两步查询（先查任务再查实例列）——JOIN 下推 DB，单查询完成。  VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。  ⚠️ 敏感视图（含审批人/审批意见列，C4）：ExposeGraphqlQuery = false，数据访问统一经 IApprovalQueryService 门面。</para> 的手写 DTO 扩展</summary>
public partial record ApprovalTaskViewDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}