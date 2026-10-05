using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Federation;

/// <summary>SSO 应用注册实体（下游 SP 接入点）——app_id + origin 白名单 + scope + client credential + per-channel HMAC 密钥。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>设计文档 §5.4/§6.3：ClientSecret 与 HmacSecret 均 AES-GCM 加密（DataService 边界加解密——      对齐 PlatformCredentialEntity 先例）；<c>HmacSecretEncrypted</c> 为 per-channel + per-app HMAC 密钥      （<c>/sso/issue</c> 验签密钥来源，Oracle P1-4——不复用 PlatformCredentialEntity）。</para></summary>
public partial class SsoClientEntity
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