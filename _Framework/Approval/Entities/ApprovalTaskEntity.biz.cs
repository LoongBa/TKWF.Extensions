using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>审批任务实体——步骤级待办（审批/驳回/转交载体）。     <para>每步骤根据审批人解析结果建一条任务（User 直接建一条 / Role 解析多人建多条）；     ApproverUserId 为解析结果快照（P1-4），避免 Role 模式重复查询。</para></summary>
public partial class ApprovalTaskEntity
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