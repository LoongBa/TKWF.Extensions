using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.AuthCenter.DTOs;

/// <summary>认证因子实体——AuthAccount 凭据白名单的因子化演进（WAVE 1：新增 SG1 数据底座，严格增量不改既有）。      <para><c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>FactorType 语义：1=Password / 2=Sms / 3=Email / 4=Federated / 5=Totp(预留) / 6=Passkey(预留)。</para>      <para>Identifier 规范化：Password=UId；Sms=E.164；Email=小写 trim；Federated=<c>{channelId}|{externalUserId}</c>。</para>      <para>SecretHash：PBKDF2/AES-GCM blob（原 AuthAccount.PasswordHash 的因子化落点）。</para>      <para>State：0=Active / 1=Unverified（解绑=物理删除——无 Revoked 态）。</para>      <para>Metadata：扩展 JSON，<b>不参与查询</b>（仅序列化存读，不做任何筛选/排序条件）。</para>      表名 <c>TKWF_AuthFactor</c>（ADR100 表名前缀——AuthCenter 表前缀批次）。 的手写 DTO 扩展</summary>
public partial record AuthFactorEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}