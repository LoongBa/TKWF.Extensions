using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.TrustCenter.DTOs;

/// <summary>密封票据实体（面① 安全数据投递/密封票据——拆分命名 2026-10-16，ADR109 决策 4 修订；V4.10.79 ADR110 有界配额 2026-10-17 落地）。      <para>自原 <c>AccessCodeEntity</c> 面①拆分：载荷密封 + 原子逐次消费（有界配额）+ TTL + 可核销人——      并入框架 <c>ISealedTicketService&lt;T&gt;</c> 的 DB CAS 后端（<c>SealedTicketService&lt;T&gt;</c>）。      只存 <c>SHA256(code)</c> 索引不存原文；<c>UsedCount &lt; MaxRedeems</c> 原子 CAS 防重放/配额耗尽；      <c>ExpectedClaimant</c> 可核销人进 CAS 条件（无 TOCTOU）。</para>      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para> 的手写 DTO 扩展</summary>
public partial record SealedTicketEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}