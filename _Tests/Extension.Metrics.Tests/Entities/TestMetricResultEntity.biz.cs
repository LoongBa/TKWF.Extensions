using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>测试持久化实体业务扩展分部（消费方模拟，标准管线）——与手写定义 + 生成的 TestMetricResultEntity.g.cs 合并；业务验证逻辑入口。</summary>
public partial class TestMetricResultEntity
{
    /// <summary>测试持久化实体业务扩展分部（消费方模拟，标准管线）——与手写定义 + 生成的 TestMetricResultEntity.g.cs 合并；业务验证逻辑入口。</summary>
    partial void OnBusinessValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：领域驱动设计的业务验证规则 
        // if (this.Status == Status.Disabled && this.Stock > 0)
        //     results.Add(new ValidationResult("禁用状态下不能有库存", new[] { nameof(Status) }));
    }
}