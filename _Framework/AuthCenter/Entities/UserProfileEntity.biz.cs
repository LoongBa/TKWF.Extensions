using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter;

/// <summary>用户档案实体——认证中心基础档案 1:1（V0.9.0 凭据/档案表级分离 A.1：Nickname/Avatar/Birthday/Gender/Email）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>红线（数据访问）：档案读写经 SG1 生成 <see cref="UserProfileEntityDataService"/>——零 IFreeSql/IEntityDAC 直注入。</para>      <para>字段语义（ADR A.5/A.6/A.7）：<b>Phone 双角色</b>——凭据角色归 <see cref="AuthAccountEntity.Phone"/>（登录锚点），      联系方式角色由业务层自决（框架不提供"复制登录号"默认操作）；<b>Email</b> 联系方式列（不启用登录凭据——A.6 fail-closed      默认关闭，Email 找回通道与 Email 登录凭据是两件事可独立启用）；<b>Gender</b> 宽松自由文本（max 32，无枚举约束——A.7）。</para>      <para>表名 <c>TKWF_UserProfile</c>（ADR100——AuthCenter 表前缀批次）。</para></summary>
public partial class UserProfileEntity
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