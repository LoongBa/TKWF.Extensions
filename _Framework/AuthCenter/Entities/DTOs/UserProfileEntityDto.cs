using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthCenter.DTOs;

/// <summary>用户档案实体——认证中心基础档案 1:1（V0.9.0 凭据/档案表级分离 A.1：Nickname/Avatar/Birthday/Gender/Email）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>红线（数据访问）：档案读写经 SG1 生成 <see cref="UserProfileEntityDataService"/>——零 IFreeSql/IEntityDAC 直注入。</para>      <para>字段语义（ADR A.5/A.6/A.7）：<b>Phone 双角色</b>——凭据角色归 <see cref="AuthAccountEntity.Phone"/>（登录锚点），      联系方式角色由业务层自决（框架不提供"复制登录号"默认操作）；<b>Email</b> 联系方式列（不启用登录凭据——A.6 fail-closed      默认关闭，Email 找回通道与 Email 登录凭据是两件事可独立启用）；<b>Gender</b> 宽松自由文本（max 32，无枚举约束——A.7）。</para>      <para>表名 <c>TKWF_UserProfile</c>（ADR100——AuthCenter 表前缀批次）。</para> 的手写 DTO 扩展</summary>
public partial record UserProfileEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}