using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter;

/// <summary>应用授权实体——OAuth2 authorization grant 语义（V0.8.0 `/grants` 端点数据底座 + B-口令兑换产品线写入点预留）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>语义：记录"用户授权某应用访问其数据的持久授权"（应用授权有效期 <see cref="ValidUntil"/>，跨会话），      与令牌生命周期（2h access / 30d refresh）正交；唯一约束 UX(UserId,AppId,Source) 防并发 exchange 产生重复 grant 行。</para></summary>
public partial class AuthGrantEntity
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