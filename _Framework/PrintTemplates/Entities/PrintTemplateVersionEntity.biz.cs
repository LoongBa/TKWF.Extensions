using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.PrintTemplates;

/// <summary>打印模板版本表实体——关联模板（TemplateId）+ 版本号（SemVer 字符串）+ 正文 + 状态。     <para>TemplateId + Version 唯一（并发发布防冲突——败者显式异常）。</para></summary>
public partial class PrintTemplateVersionEntity
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