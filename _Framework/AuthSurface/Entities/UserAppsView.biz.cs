using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>我的应用读模型（跨扩展 VEntity——视图 <c>TKWFV_UserApps</c>）。  <para>JOIN AuthCenter <c>AuthGrant</c>（应用授权底座）+ <c>AuthAccount</c>（<c>UserId = UId</c> 行级 FK）+ 本扩展 <c>AuthApp</c>（LEFT JOIN 取 AppName——  目录未录入的应用显示裸 AppId 而非丢行）。</para>  <para>⚠️ 视图不过滤 <c>Status</c>/<c>ValidUntil</c>（"我的应用"有效过滤在 DataService——C# 侧 UtcNow 无 CURRENT_TIMESTAMP 方言风险；  视图保留全量便于审计与 v0.2.0 授权快照）。ViewSql 零 <c>vw_</c> 嵌套（视图链禁令 VIEW002 合规——JOIN 均为基表）。</para></summary>
public partial class UserAppsView
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