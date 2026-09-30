using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.MFA.DTOs;

/// <summary>MFA 绑定实体——用户×方法绑定记录（启用 = 有已激活记录）。  <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；保留 BCL <c>[Table("MfaSecret")]</c>；  列映射用 FreeSql <c>[Column]</c>（全限定）。</para>  <para>双方法一实体（Oracle Q5 裁决）：TOTP 场景 <c>SecretEncrypted</c>（AES-GCM 密文——DataService 边界加解密，  对齐 <c>PlatformCredentialKeyStore</c> 先例）；SMS 场景 <c>Phone</c>（绑手机）；另一列 null（列可空）。</para>  <para>绑定生命周期（ADR-MFA-挑战票据与验证模型 + Oracle C3）：<c>EnrollAsync</c> 写入（IsConfirmed=false +  EnrollTokenHash + EnrollExpireAt）→ <c>ConfirmEnrollAsync</c> 校验一次码后翻转 IsConfirmed + 清空 enrollToken；  激活后 <c>SecretEncrypted</c>/<c>Phone</c> 不可改（CanUpdate=false——解绑重绑走 Disable+Enroll，ADR 定案）。</para>  <para>索引：<c>UX_MfaSecret_User_Method</c>（UserId+Method 联合唯一——每用户每方法一条绑定）。</para> 的手写 DTO 扩展</summary>
public partial record MfaSecretEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}