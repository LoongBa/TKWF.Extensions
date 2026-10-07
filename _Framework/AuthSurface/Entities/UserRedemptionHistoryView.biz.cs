using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>兑换历史读模型（跨扩展 VEntity——视图 <c>TKWFV_UserRedemptionHistory</c>）。  <para>JOIN 本扩展 <c>RedemptionCode</c> + AuthCenter <c>AuthAccount</c>（按 <c>RedeemedByUId = UId</c> 行级 FK，P4）——  视图归授权面（业务域扩展，AGENTS §8）；<c>TKWFV_</c> 前缀暂用（前置裁定 P3 / ADR-AuthCenter C.16，框架组前缀批次核查后视情况 rename）。</para>  <para>⚠️ 视图不过滤（历史完整性）——<c>Status=1</c>（Redeemed）过滤在 DataService；<c>INNER JOIN</c> 天然排除未兑换行  （<c>RedeemedByUId</c> NULL 匹配不到 AuthAccount）。</para></summary>
public partial class UserRedemptionHistoryView
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