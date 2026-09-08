using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>作业执行历史实体（V0.1.0）——每次执行一行，记录耗时/重试/异常归档。</summary>
public partial class JobExecutionEntity
{
    /// <summary>
    /// 根据需要添加业务验证逻辑 (例如跨表验证、状态机检查)
    /// </summary>
    partial void OnBusinessValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：领域驱动设计的业务验证规则
        // if (this.CompletedAtUtc < this.StartedAtUtc)
        //     results.Add(new ValidationResult("完成时间不能早于开始时间", new[] { nameof(CompletedAtUtc) }));
    }
}
