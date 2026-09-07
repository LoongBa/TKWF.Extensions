using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Tagging;

/// <summary>标签命中记录实体（V0.3.0）——持久化 <c>TKW.Framework.Utility.Tags.TagHit</c> 结果，支撑"高频标签/时间分布/维度占比"分析。     <para>字段对齐 <c>TagHit</c> record（算法输出模型）+ 原文快照/时间戳；<c>[DomainGenerateCode]</c> 不指定 UserType     （ADR42 D4）；HitTime 用 DateTime（UTC）——FreeSql SQLite 不支持 DateTimeOffset（Oracle P1-1）。</para></summary>
public partial class TagHitRecordEntity
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
