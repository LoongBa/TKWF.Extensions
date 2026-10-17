using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>回复规则实体（表 <c>TKWF_WeChatReplyRule</c>）——通用回复规则表（Oracle P1 泛化：原 KeywordRule/ClickReplyMap/WelcomeConfig 合并）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>SceneType（int）：1=Subscribe 欢迎语 / 2=Keyword 关键词 / 3=Click 菜单 key 映射 / 4=Image / 5=Voice / 6=Video /      7=ShortVideo / 8=Location / 9=Link / 10=Scan 扫码 / 11=Default 兜底。</para>      <para><see cref="TriggerKey"/>：关键词（Keyword）或菜单 key（Click）——场景型规则（Subscribe/Image/.../Scan/Default）为空。      <see cref="MatchMode"/> 仅 Keyword 场景适用（1=Exact 完全匹配 / 2=Contains 包含匹配）。      <see cref="ReplyText"/>（直接文本）优先于 <see cref="MaterialId"/>（素材引用）——存在 ReplyText 用 ReplyText，否则走素材。</para>      <para>⚠️ Click 映射独立于菜单结构：SceneType=Click 的规则修改<b>不触发</b> menu/create（Oracle P1 语义保留）。</para></summary>
public partial class ReplyRuleEntity
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