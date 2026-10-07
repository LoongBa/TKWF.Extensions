using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>兑换码实体（表 <c>RedemptionCode</c>）——口令兑换体系数据属主（授权面）。  <para>单实体承载生成/兑换/历史（v0.1.0 无批次——一码一兑语义下"兑换记录 = 码被兑换态"；  v0.2.0 批次管理端加 <c>BatchId</c> 列即可扩展）。</para>  <para>🔒 安全：code 明文<b>不落库</b>——<see cref="CodeHash"/>（SHA256 十六进制 64，唯一，防库泄露即任意兑换）；  展示经 <see cref="CodeMasked"/>（脱敏值）。行级 FK = <c>AuthAccount.UId</c>（前置裁定 P4——<see cref="RedeemedByUId"/>）。</para></summary>
public partial class RedemptionCodeEntity
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