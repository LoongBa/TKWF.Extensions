using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthSurface.DTOs;

/// <summary>应用目录实体（表 <c>AuthApp</c>）——"我的应用"聚合的 AppName 来源。  <para>背景：AuthCenter <c>AuthGrantEntity</c> 底座<b>无应用元数据</b>（仅 AppId 裸 id——explore 实证）；  独立目录表 = OAuth2 client 注册表语义（长期演进清晰），不动 AuthCenter（跨扩展零改）。</para>  <para>⚠️ 软引用语义：<see cref="AppId"/> 与 <c>AuthGrant.AppId</c> / <c>RedemptionCode.TargetAppId</c> 为字符串约定  （无 FK 约束）——删除前检查引用；建议 <c>SetEnabledAsync(false)</c> 软禁用而非物理删除（防 vm_UserApps 丢 AppName）。</para> 的手写 DTO 扩展</summary>
public partial record AuthAppEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}