using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.DataDictionary;

/// <summary>数据字典项-定义视图实体（VEntity）——跨表 JOIN <c>DictionaryItem</c> → <c>DictionaryDefinition</c>，  按 DefinitionCode 单查询返回项 + 定义列（读模型联邦）。  <para>V0.2.0：替代 <c>GetOrLoadAggregateAsync</c> 项查询两步（先按 DefinitionId 查项）——JOIN 下推 DB，单查询完成。  VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。  ⚠️ 树语义保护：ExposeGraphqlQuery = false（裸平表绕过 BuildTree 树语义），数据访问统一经 IDictionaryManager 门面。  ⚠️ INNER JOIN 语义："定义存在但无项"返回零行——Manager 先单查定义（不存在→null），再走视图查项（零行=空项列表），  "空字典"语义保留（oracle3 C-1/H1 方案 b）。</para></summary>
public partial class DictionaryItemView
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