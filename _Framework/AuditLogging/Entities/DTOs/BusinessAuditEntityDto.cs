using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuditLogging.DTOs;

/// <summary>业务事实审计表实体——一行 = 一次业务对象变更事实（Actor + Target + Action + Before/After）。      <para>与 <see cref="AuditLogEntity"/>（方法调用审计）分立：<b>写模型分立，每面单一事实源</b>（P2）。      内容列显式 <c>[MaxLength(-1)]</c>（AGENTS §8 2026-11-14 事故红线——防 Pg 22001）。</para>      <para><b>默认 append-only</b>（ADR-AuditLogging-不可抵赖与删除策略）——标准 CRUD 暴露面在 DataService 分部排除      <c>BeforeJson</c>/<c>AfterJson</c>（比照 V0.4.0 <c>ArgumentsJson</c> ExcludeMethods 先例，防 PII）。</para> 的手写 DTO 扩展</summary>
public partial record BusinessAuditEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}