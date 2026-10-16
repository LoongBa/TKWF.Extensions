using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.TrustCenter;

/// <summary>密封票据实体（面① 安全数据投递/密封票据——拆分命名 2026-10-16，ADR109 决策 4 修订）。      <para>自原 <c>AccessCodeEntity</c> 面①拆分：载荷密封 + 单次兑换 + TTL + 可核销人——      并入框架 <c>ISealedTicketService&lt;T&gt;</c> 的 DB CAS 后端（<c>SealedTicketService&lt;T&gt;</c>）。      只存 <c>SHA256(code)</c> 索引不存原文；<c>used</c> 原子 CAS 防重放；      <c>ExpectedClaimant</c> 可核销人进 CAS 条件（无 TOCTOU）。</para>      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para></summary>
public partial class SealedTicketEntity
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