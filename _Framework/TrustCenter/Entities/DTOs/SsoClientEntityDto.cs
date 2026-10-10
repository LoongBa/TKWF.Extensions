using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.TrustCenter.DTOs;

/// <summary>SSO 应用注册实体（下游 SP 接入点）——app_id + origin 白名单 + scope + client credential + per-channel HMAC 密钥。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>设计文档 §5.4/§6.3：ClientSecret 与 HmacSecret 均 AES-GCM 加密（DataService 边界加解密——      对齐 PlatformCredentialEntity 先例）；<c>HmacSecretEncrypted</c> 为 per-channel + per-app HMAC 密钥      （<c>/sso/issue</c> 验签密钥来源，Oracle P1-4——不复用 PlatformCredentialEntity）。</para> 的手写 DTO 扩展</summary>
public partial record SsoClientEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}