using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Tagging.DTOs;

/// <summary>标签命中记录实体（V0.3.0）——持久化 <c>TKW.Framework.Utility.Tags.TagHit</c> 结果，支撑"高频标签/时间分布/维度占比"分析。     <para>字段对齐 <c>TagHit</c> record（算法输出模型）+ 原文快照/时间戳；<c>[DomainGenerateCode]</c> 不指定 UserType     （ADR42 D4）；HitTime 用 DateTime（UTC）——FreeSql SQLite 不支持 DateTimeOffset（Oracle P1-1）。</para> 的手写 DTO 扩展</summary>
public partial record TagHitRecordEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}
