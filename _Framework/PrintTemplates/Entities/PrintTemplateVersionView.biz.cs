using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.PrintTemplates;

/// <summary>打印模板版本视图实体（VEntity）——跨表 JOIN <c>PrintTemplateVersion</c> → <c>PrintTemplate</c>，      携带模板 <c>Key</c>/<c>TemplateName</c>，按 Key 单查询返回版本。      <para>V0.2.0：替代两步查询（先 GetByKeyAsync 取 TemplateId 再查版本）——JOIN 下推 DB，单查询完成，      并补足当前返回面缺失的模板名（免第 3 次往返）。RenderAsync 从 2 次往返降为 1 次。      VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。</para>      <para>⚠️ 敏感视图（C4）：含模板正文 <c>Content</c>（商业资产）——<c>ExposeGraphqlQuery = false</c>，      数据访问统一经 <see cref="ITemplateManager"/> 门面（门面内已有状态机/权限语义）。</para></summary>
public partial class PrintTemplateVersionView
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