using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter;

/// <summary>密码重置码实体（V0.9.0 B.10/B.11——找回多通道自建链路落库底座）。      <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（ADR42 D4，供任意装配实例）。</para>      <para>⚠️ 与 Account 扩展 <c>PasswordResetCodeEntity</c>（userName-keyed，表 <c>PasswordResetCode</c>）<b>同名不同命名空间</b>      （本类 <c>TKWF.Ext.AuthCenter</c> / 表 <c>TKWF_PasswordResetCode</c>）——ADR B.10 明确 AuthCenter 自建 UId-keyed 链路      （绑定 Account 契约按 userName 写 IdentityUser，与 AuthAccount UId/Phone 模型平行不互认），两实体零冲突。</para>      <para>CodeHash 单向散列（SHA256 hex——不存明文；对标 SmsRecord.CodeHash）；多通道字段 Channel=SMS/Email/OAuthTicket      （B.10 层级区分：验证码通道可达 / 扫码身份持有证明）；自带投递（B.11 修复 Account 侧 InitiateResetAsync 生成码不投递缺口）。</para>      <para>表名 <c>TKWF_PasswordResetCode</c>（ADR100——AuthCenter 表前缀批次）。</para></summary>
public partial class PasswordResetCodeEntity
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