using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.Authentication.DTOs;

/// <summary>一次性票据实体——票据换令牌（TTL 5min 单次消费 + PKCE + app_id/redirect_uri 白名单 + state 防重放）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>回调承载铁律（用户裁定 + Oracle B1）：URL 只带一次性票据 + redirect_uri，绝不带敏感信息；纯前端静态站走公网 /oauth/exchange + PKCE code_verifier。</para> 的手写 DTO 扩展</summary>
public partial record OAuthTicketEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}