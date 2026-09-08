using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.BackgroundJobs.DTOs;

/// <summary>业务结果实体（V0.1.0）——作业内显式记录业务产出，经 IJobResultRecorder 落库。的手写 DTO 扩展</summary>
public partial record JobResultEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (string.IsNullOrEmpty(this.ResultJson) && string.IsNullOrEmpty(this.Summary))
        //    results.Add(new ValidationResult("业务产出 JSON 与摘要至少填一项", new[] { nameof(ResultJson) }));
    }
}
