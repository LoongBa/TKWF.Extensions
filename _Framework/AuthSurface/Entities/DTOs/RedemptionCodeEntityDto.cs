using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthSurface.DTOs;

/// <summary>兑换码实体（表 <c>RedemptionCode</c>）——口令兑换体系数据属主（授权面）。  <para>单实体承载生成/兑换/历史（v0.1.0 无批次——一码一兑语义下"兑换记录 = 码被兑换态"；  v0.2.0 批次管理端加 <c>BatchId</c> 列即可扩展）。</para>  <para>🔒 安全：code 明文<b>不落库</b>——<see cref="CodeHash"/>（SHA256 十六进制 64，唯一，防库泄露即任意兑换）；  展示经 <see cref="CodeMasked"/>（脱敏值）。行级 FK = <c>AuthAccount.UId</c>（前置裁定 P4——<see cref="RedeemedByUId"/>）。</para> 的手写 DTO 扩展</summary>
public partial record RedemptionCodeEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}