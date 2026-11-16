using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>兑换码批次实体（表 <c>TKWF_CodeBatch</c>，v0.3.0 批次管理端）——兑换码域固有聚合根「头表」。  <para>「头+行」均在扩展内（对齐 DataPort 任务/记录、FileManagement 主/版本先例）：批次计划数量/状态/统计基座承载于头，  码行 <see cref="RedemptionCodeEntity.BatchId"/> 软引用本表 <c>Id</c>（<c>long?</c> 直接存 Id，对齐  <c>ManagedFileVersionEntity.FileId</c> 先例——非唯一索引，代码级生命周期保证：批次仅 DRAFT 可删，DRAFT 无码）。</para>  <para>状态机：DRAFT(0) → GENERATING(1，CAS 抢占) → GENERATED(2)；GENERATED → REVOKED(3，整批吊销动作)；  DRAFT →（物理删除）；失败回滚 → DRAFT。PARTIAL_REVOKED 不落库（派生：Status==2 且 revokedCount&gt;0）。</para></summary>
public partial class CodeBatchEntity
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