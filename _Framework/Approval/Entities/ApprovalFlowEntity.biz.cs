using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>审批流定义实体——持久化审批流程定义（Code 唯一 + 顺序步骤链定义 + 或签/会签模式）。     <para>[DomainGenerateCode] 不指定 UserType（ADR42 D4——扩展不自建 UserInfo）；     审计字段用 DateTime（UTC）——FreeSql SQLite 不支持 DateTimeOffset。</para></summary>
public partial class ApprovalFlowEntity
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