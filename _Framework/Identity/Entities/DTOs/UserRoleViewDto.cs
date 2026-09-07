using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Identity.DTOs;

/// <summary>用户-角色视图实体（VEntity）——跨表 JOIN <c>IdentityUserRole</c> → <c>IdentityRole</c>，按 UserId 单查询返回角色。     <para>V0.2.0：替代两步查询（先查 RoleId 集合再查 Role）——JOIN 下推 DB，单查询完成。     VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。</para> 的手写 DTO 扩展</summary>
public partial record UserRoleViewDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}