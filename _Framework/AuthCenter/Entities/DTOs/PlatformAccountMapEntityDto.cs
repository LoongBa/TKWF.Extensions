using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthCenter.DTOs;

/// <summary>跨系统映射实体——平台内部 id ↔ 业务 app + 业务本地 id + UnionId（统一 DMP 双机制：映射表/外键）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>DMP V4.0 管理员走映射表、会员走外键——Ext 以本表统一承载；DMP 迁移时会员映射迁入本表（需求 §6 约束 1）。</para> 的手写 DTO 扩展</summary>
public partial record PlatformAccountMapEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}