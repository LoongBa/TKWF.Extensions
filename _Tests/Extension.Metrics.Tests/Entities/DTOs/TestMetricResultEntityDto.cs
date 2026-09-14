using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Metrics.Tests.DTOs;

/// <summary>测试持久化实体 DTO（消费方模拟，标准管线）——手写扩展分部，与生成的 TestMetricResultEntityDto.g.cs（partial record）合并；自定义验证逻辑入口。</summary>
public partial record TestMetricResultEntityDto
{
    /// <summary>测试持久化实体 DTO（消费方模拟，标准管线）——手写扩展分部，与生成的 TestMetricResultEntityDto.g.cs（partial record）合并；自定义验证逻辑入口。</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}