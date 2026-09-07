using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Tagging;

/// <summary>标签规则实体（V0.3.0）——持久化标签匹配规则，供 <see cref="T:TKWF.Ext.Tagging.ITagRuleStore"/> 管理 + <c>ITagService.LoadRules</c> 供给算法。     <para>字段对齐 <c>TKW.Framework.Utility.Tags.TagRule</c>（算法输入模型）；<c>[DomainGenerateCode]</c> 不指定 UserType     （ADR42 D4——扩展不自建 UserInfo）；审计字段用 DateTime（UTC）——FreeSql SQLite 不支持 DateTimeOffset（Oracle P1-1）。</para></summary>
public partial class TagRuleEntity
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
