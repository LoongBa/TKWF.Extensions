using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;
namespace TKWF.Ext.MFA.DTOs;

/// <summary>MFA 恢复码实体——防锁死（验证器/手机丢失时凭恢复码解绑重绑）。  <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；保留 BCL <c>[Table("MfaRecoveryCode")]</c>；  列映射用 FreeSql <c>[Column]</c>（全限定）。</para>  <para>恢复码语义：8 位字母数字（去易混淆字符 0/O/1/I/l，Oracle P9）SHA256 落库（明文不落库）、单次消费  （<c>IsConsumed</c> 翻转）、再生成 = 全量替换（删旧码插新码）；验证纳入 per-user×method 频控（Oracle C4）。</para>  <para>索引：<c>UX_MfaRecovery_User_CodeHash</c>（UserId+CodeHash 联合唯一——码唯一防碰撞）。</para> 的手写 DTO 扩展</summary>
public partial record MfaRecoveryCodeEntityDto
{
    /// <summary>根据需要添加自定义验证逻辑</summary>
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：非数据库依赖的字段交叉验证
        // if (this.StartTime > this.EndTime) 
        //    results.Add(new ValidationResult("开始时间不能晚于结束时间", new[] { nameof(StartTime) }));
    }
}