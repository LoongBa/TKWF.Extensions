using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.OrganizationUnit;

/// <summary>组织单元-用户关联实体（用户 ↔ OU 多对多 junction）。     <para>唯一约束 UX_OrganizationUnitUser_User_OU 防重复分配——并发重复分配冲突由     <see cref="M:TKWF.Ext.OrganizationUnit.IOrganizationUnitManager.AssignUserAsync(System.Int64,System.String,System.Threading.CancellationToken)"/> 捕获数据库异常转业务异常。</para></summary>
public partial class OrganizationUnitUserEntity
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