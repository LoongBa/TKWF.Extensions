using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthCenter.DTOs;

/// <summary>刷新令牌实体——Refresh rotation（SHA256 落库，新旧不可复用 + TokenVersion 闭环）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>令牌明文不落库——仅存 SHA256 hex；刷新时校验 TokenVersion 匹配（不匹配 → 拒绝，闭环 DMP 缺口）。</para> 的手写 DTO 扩展</summary>
public partial record AuthRefreshTokenEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}