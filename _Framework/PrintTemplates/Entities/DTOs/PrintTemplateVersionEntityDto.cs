using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.PrintTemplates.DTOs;

/// <summary>打印模板版本表实体——关联模板（TemplateId）+ 版本号（SemVer 字符串）+ 正文 + 状态。     <para>TemplateId + Version 唯一（并发发布防冲突——败者显式异常）。</para> 的手写 DTO 扩展</summary>
public partial record PrintTemplateVersionEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}