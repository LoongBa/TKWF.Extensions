using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>菜单版本实体（表 <c>TKWF_WeChatMenuVersion</c>）——菜单发布历史（内容版本——before/after JSON + 发布人/结果，回滚数据源）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>职责分离（Oracle P1）：<b>内容版本</b>（before/after 完整 JSON + 发布审计人/结果）归本表；      操作审计（who/when/action，不含 JSON）归 <c>MenuPublishLogEntity</c>——发布动作两者同时记录，非发布操作只记 PublishLog。</para>      <para>Result：1=Success / 2=Fail——回滚时加载旧版本 AfterJson 重新发布。</para></summary>
public partial class MenuVersionEntity
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