using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter;

/// <summary>认证因子实体——AuthAccount 凭据白名单的因子化演进（WAVE 1：新增 SG1 数据底座，严格增量不改既有）。      <para><c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>      <para>FactorType 语义：1=Password / 2=Sms / 3=Email / 4=Federated / 5=Totp(预留) / 6=Passkey(预留)。</para>      <para>Identifier 规范化：Password=UId；Sms=E.164；Email=小写 trim；Federated=<c>{channelId}|{externalUserId}</c>。</para>      <para>SecretHash：PBKDF2/AES-GCM blob（原 AuthAccount.PasswordHash 的因子化落点）。</para>      <para>State：0=Active / 1=Unverified（解绑=物理删除——无 Revoked 态）。</para>      <para>Metadata：扩展 JSON，<b>不参与查询</b>（仅序列化存读，不做任何筛选/排序条件）。</para>      表名 <c>TKWF_AuthFactor</c>（ADR100 表名前缀——AuthCenter 表前缀批次）。</summary>
public partial class AuthFactorEntity
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