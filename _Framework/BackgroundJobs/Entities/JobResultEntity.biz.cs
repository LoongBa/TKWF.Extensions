using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>业务结果实体（V0.1.0）——作业内显式记录业务产出，经 IJobResultRecorder 落库。</summary>
public partial class JobResultEntity
{
    /// <summary>
    /// 根据需要添加业务验证逻辑 (例如跨表验证、状态机检查)
    /// </summary>
    partial void OnBusinessValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：领域驱动设计的业务验证规则
        // if (string.IsNullOrEmpty(this.ResultJson) && string.IsNullOrEmpty(this.Summary))
        //     results.Add(new ValidationResult("业务产出 JSON 与摘要至少填一项", new[] { nameof(ResultJson) }));
    }
}
