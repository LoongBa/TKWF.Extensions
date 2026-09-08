using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.OrganizationUnit.DTOs;

/// <summary>组织单元-用户关联 DTO——手写扩展骨架（DTO 主体 + IDomainDto 实现由 xCodeGen 生成的 .g.cs 承载）。</summary>
public partial record OrganizationUnitUserEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime)
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}
