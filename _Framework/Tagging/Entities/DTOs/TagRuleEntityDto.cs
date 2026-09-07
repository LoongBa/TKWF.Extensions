using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Tagging.DTOs;

/// <summary>标签规则实体（V0.3.0）——持久化标签匹配规则，供 <see cref="T:TKWF.Ext.Tagging.ITagRuleStore"/> 管理 + <c>ITagService.LoadRules</c> 供给算法。     <para>字段对齐 <c>TKW.Framework.Utility.Tags.TagRule</c>（算法输入模型）；<c>[DomainGenerateCode]</c> 不指定 UserType     （ADR42 D4——扩展不自建 UserInfo）；审计字段用 DateTime（UTC）——FreeSql SQLite 不支持 DateTimeOffset（Oracle P1-1）。</para> 的手写 DTO 扩展</summary>
public partial record TagRuleEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}
