using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.BackgroundJobs.DTOs;

/// <summary>作业执行历史实体（V0.1.0）——每次执行一行，记录耗时/重试/异常归档。的手写 DTO 扩展</summary>
public partial record JobExecutionEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartedAtUtc > this.CompletedAtUtc)
        //    results.Add(new ValidationResult("开始时间不能晚于完成时间", new[] { nameof(StartedAtUtc) }));
    }
}
