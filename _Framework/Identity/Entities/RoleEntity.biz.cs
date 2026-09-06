using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Identity;

/// <summary>角色实体——角色名、显示名与系统角色标记。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；     保留 BCL <c>[Table("IdentityRole")]</c>；列映射用 FreeSql <c>[Column]</c>。</para></summary>
public partial class RoleEntity
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