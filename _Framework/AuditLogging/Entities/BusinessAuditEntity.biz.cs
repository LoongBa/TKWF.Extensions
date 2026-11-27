using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuditLogging;

/// <summary>业务事实审计表实体——一行 = 一次业务对象变更事实（Actor + Target + Action + Before/After）。      <para>与 <see cref="AuditLogEntity"/>（方法调用审计）分立：<b>写模型分立，每面单一事实源</b>（P2）。      内容列显式 <c>[MaxLength(-1)]</c>（AGENTS §8 2026-11-14 事故红线——防 Pg 22001）。</para>      <para><b>默认 append-only</b>（ADR-AuditLogging-不可抵赖与删除策略）——标准 CRUD 暴露面在 DataService 分部排除      <c>BeforeJson</c>/<c>AfterJson</c>（比照 V0.4.0 <c>ArgumentsJson</c> ExcludeMethods 先例，防 PII）。</para></summary>
public partial class BusinessAuditEntity
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