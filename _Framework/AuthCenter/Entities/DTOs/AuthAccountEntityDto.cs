using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthCenter.DTOs;

/// <summary>平台账号实体——认证中心身份源（「你是谁」：手机号主键 + 微信绑定 + 认证声明，不含业务角色）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>JWT <c>sub=user:{UId}</c> 引用本实体平台内部 id；业务角色由各业务系统 <c>IAuthorizationMapper</c> 本地映射。</para> 的手写 DTO 扩展</summary>
public partial record AuthAccountEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}