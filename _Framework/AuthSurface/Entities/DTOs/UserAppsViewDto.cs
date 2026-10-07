using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthSurface.DTOs;

/// <summary>我的应用读模型（跨扩展 VEntity——视图 <c>TKWFV_UserApps</c>）。  <para>JOIN AuthCenter <c>AuthGrant</c>（应用授权底座）+ <c>AuthAccount</c>（<c>UserId = UId</c> 行级 FK）+ 本扩展 <c>AuthApp</c>（LEFT JOIN 取 AppName——  目录未录入的应用显示裸 AppId 而非丢行）。</para>  <para>⚠️ 视图不过滤 <c>Status</c>/<c>ValidUntil</c>（"我的应用"有效过滤在 DataService——C# 侧 UtcNow 无 CURRENT_TIMESTAMP 方言风险；  视图保留全量便于审计与 v0.2.0 授权快照）。ViewSql 零 <c>vw_</c> 嵌套（视图链禁令 VIEW002 合规——JOIN 均为基表）。</para> 的手写 DTO 扩展</summary>
public partial record UserAppsViewDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}