using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthSurface.DTOs;

/// <summary>兑换码批次实体（表 <c>TKWF_CodeBatch</c>，v0.3.0 批次管理端）——兑换码域固有聚合根「头表」。  <para>「头+行」均在扩展内（对齐 DataPort 任务/记录、FileManagement 主/版本先例）：批次计划数量/状态/统计基座承载于头，  码行 <see cref="RedemptionCodeEntity.BatchId"/> 软引用本表 <c>Id</c>（<c>long?</c> 直接存 Id，对齐  <c>ManagedFileVersionEntity.FileId</c> 先例——非唯一索引，代码级生命周期保证：批次仅 DRAFT 可删，DRAFT 无码）。</para>  <para>状态机：DRAFT(0) → GENERATING(1，CAS 抢占) → GENERATED(2)；GENERATED → REVOKED(3，整批吊销动作)；  DRAFT →（物理删除）；失败回滚 → DRAFT。PARTIAL_REVOKED 不落库（派生：Status==2 且 revokedCount&gt;0）。</para> 的手写 DTO 扩展</summary>
public partial record CodeBatchEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}