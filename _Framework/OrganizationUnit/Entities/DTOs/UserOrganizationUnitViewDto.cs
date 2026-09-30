using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.OrganizationUnit.DTOs;

/// <summary>组织单元-用户关联视图实体（VEntity）——跨表 JOIN <c>OrganizationUnitUser</c> → <c>OrganizationUnit</c>，  按 OUPath 前缀单查询返回用户归属 + OU 列（读模型联邦，V0.2.0 下推）。  <para>V0.2.0：替代 <c>GetUserIdsInOrganizationUnitAsync(includeDescendants: true)</c> 两步查询  （先取子树 OU Id 集合 → 再 junction 查 UserIds）——JOIN + Path 前缀谓词下推 DB，单查询完成。  VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。  ⚠️ 敏感视图（含用户归属明细）：ExposeGraphqlQuery = false，数据访问统一经 IOrganizationUnitManager 门面。</para> 的手写 DTO 扩展</summary>
public partial record UserOrganizationUnitViewDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}