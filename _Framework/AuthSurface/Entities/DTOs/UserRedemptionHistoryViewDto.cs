using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthSurface.DTOs;

/// <summary>兑换历史读模型（跨扩展 VEntity——视图 <c>TKWFV_UserRedemptionHistory</c>）。  <para>JOIN 本扩展 <c>RedemptionCode</c> + AuthCenter <c>AuthAccount</c>（按 <c>RedeemedByUId = UId</c> 行级 FK，P4）——  视图归授权面（业务域扩展，AGENTS §8）；<c>TKWFV_</c> 前缀暂用（前置裁定 P3 / ADR-AuthCenter C.16，框架组前缀批次核查后视情况 rename）。</para>  <para>⚠️ 视图不过滤（历史完整性）——<c>Status=1</c>（Redeemed）过滤在 DataService；<c>INNER JOIN</c> 天然排除未兑换行  （<c>RedeemedByUId</c> NULL 匹配不到 AuthAccount）。</para> 的手写 DTO 扩展</summary>
public partial record UserRedemptionHistoryViewDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}