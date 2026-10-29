using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations;

/// <summary>运营配置·票据域（表 <c>TKWF_OpsConfig_Ticket</c>）——D03 1.10 运营配置面（按域多表 O4-4：各域独立版本/并发写/schema 演进）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>      <para>密封票据行为策略（D03 1.3 签票载荷切换 + 本次运营配置化）：      <see cref="Mode"/>（Sealed | Loose——仅 Sealed 本期可用，O3 定稿）/      <see cref="TtlSeconds"/> 票据有效期（默认 300）。</para>      <para>乐观并发（R7）：<see cref="Version"/> 为并发令牌——PUT 带 expected version，冲突 409；<see cref="UpdatedAt"/>/<see cref="UpdatedBy"/> 写路径统一填充。</para></summary>
public partial class TicketOpsConfigEntity
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