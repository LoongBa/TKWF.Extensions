using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>菜单操作审计实体（表 <c>TKWF_WeChatMenuPublishLog</c>）——操作审计（who/when/action，<b>不含 JSON 内容</b>——JSON 归 MenuVersion）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>职责分离（Oracle P1）：发布动作 MenuVersion（记内容）+ MenuPublishLog（记审计）同时记录；      非发布操作（读取/删除/测试）只记本表。</para>      <para>Action（int）：1=Read（拉取当前菜单）/ 2=Create（发布）/ 3=Delete（删除）/ 4=Test（试发测试）。</para></summary>
public partial class MenuPublishLogEntity
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