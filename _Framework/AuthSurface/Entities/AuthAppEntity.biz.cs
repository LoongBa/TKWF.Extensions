using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>应用目录实体（表 <c>AuthApp</c>）——"我的应用"聚合的 AppName 来源。  <para>背景：AuthCenter <c>AuthGrantEntity</c> 底座<b>无应用元数据</b>（仅 AppId 裸 id——explore 实证）；  独立目录表 = OAuth2 client 注册表语义（长期演进清晰），不动 AuthCenter（跨扩展零改）。</para>  <para>⚠️ 软引用语义：<see cref="AppId"/> 与 <c>AuthGrant.AppId</c> / <c>RedemptionCode.TargetAppId</c> 为字符串约定  （无 FK 约束）——删除前检查引用；建议 <c>SetEnabledAsync(false)</c> 软禁用而非物理删除（防 vm_UserApps 丢 AppName）。</para></summary>
public partial class AuthAppEntity
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