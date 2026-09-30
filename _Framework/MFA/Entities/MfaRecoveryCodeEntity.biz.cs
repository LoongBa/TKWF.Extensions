using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.MFA;

/// <summary>MFA 恢复码实体——防锁死（验证器/手机丢失时凭恢复码解绑重绑）。  <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；保留 BCL <c>[Table("MfaRecoveryCode")]</c>；  列映射用 FreeSql <c>[Column]</c>（全限定）。</para>  <para>恢复码语义：8 位字母数字（去易混淆字符 0/O/1/I/l，Oracle P9）SHA256 落库（明文不落库）、单次消费  （<c>IsConsumed</c> 翻转）、再生成 = 全量替换（删旧码插新码）；验证纳入 per-user×method 频控（Oracle C4）。</para>  <para>索引：<c>UX_MfaRecovery_User_CodeHash</c>（UserId+CodeHash 联合唯一——码唯一防碰撞）。</para></summary>
public partial class MfaRecoveryCodeEntity
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