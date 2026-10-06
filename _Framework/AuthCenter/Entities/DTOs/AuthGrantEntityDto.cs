using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthCenter.DTOs;

/// <summary>应用授权实体——OAuth2 authorization grant 语义（V0.8.0 `/grants` 端点数据底座 + B-口令兑换产品线写入点预留）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>语义：记录"用户授权某应用访问其数据的持久授权"（应用授权有效期 <see cref="ValidUntil"/>，跨会话），      与令牌生命周期（2h access / 30d refresh）正交；唯一约束 UX(UserId,AppId,Source) 防并发 exchange 产生重复 grant 行。</para> 的手写 DTO 扩展</summary>
public partial record AuthGrantEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}