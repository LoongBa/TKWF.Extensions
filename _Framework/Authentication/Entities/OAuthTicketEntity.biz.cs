using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Authentication;

/// <summary>一次性票据实体——票据换令牌（TTL 5min 单次消费 + PKCE + app_id/redirect_uri 白名单 + state 防重放）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>      <para>回调承载铁律（用户裁定 + Oracle B1）：URL 只带一次性票据 + redirect_uri，绝不带敏感信息；纯前端静态站走公网 /oauth/exchange + PKCE code_verifier。</para></summary>
public partial class OAuthTicketEntity
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