using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Identity;

/// <summary>用户-角色视图实体（VEntity）——跨表 JOIN <c>IdentityUserRole</c> → <c>IdentityRole</c>，按 UserId 单查询返回角色。     <para>V0.2.0：替代两步查询（先查 RoleId 集合再查 Role）——JOIN 下推 DB，单查询完成。     VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。</para></summary>
public partial class UserRoleView
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