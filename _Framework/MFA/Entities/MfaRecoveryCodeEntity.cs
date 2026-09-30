using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA 恢复码实体——防锁死（验证器/手机丢失时凭恢复码解绑重绑）。
/// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；保留 BCL <c>[Table("MfaRecoveryCode")]</c>；
/// 列映射用 FreeSql <c>[Column]</c>（全限定）。</para>
/// <para>恢复码语义：8 位字母数字（去易混淆字符 0/O/1/I/l，Oracle P9）SHA256 落库（明文不落库）、单次消费
/// （<c>IsConsumed</c> 翻转）、再生成 = 全量替换（删旧码插新码）；验证纳入 per-user×method 频控（Oracle C4）。</para>
/// <para>索引：<c>UX_MfaRecovery_User_CodeHash</c>（UserId+CodeHash 联合唯一——码唯一防碰撞）。</para>
/// </summary>
[Table("MfaRecoveryCode")]
[FreeSql.DataAnnotations.Index("UX_MfaRecovery_User_CodeHash", nameof(UserId) + "," + nameof(CodeHash), IsUnique = true)]
[FreeSql.DataAnnotations.Index("IX_MfaRecovery_User", nameof(UserId))]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class MfaRecoveryCodeEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>归属用户 Id。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(128)]
    public string UserId { get; set; } = "";

    /// <summary>恢复码 SHA256（十六进制小写——明文不落库；写入后不可变）。
    /// 敏感字段：Dto 忽略 + Json 忽略（tkwf-entity 规则 4——哈希泄露允许离线字典攻击）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3, CanUpdate = false)]
    [MaxLength(64)]
    [DtoFieldIgnore]
    [JsonIgnore]
    public string CodeHash { get; set; } = "";

    /// <summary>是否已消费（使用后翻转 true——单次消费，Oracle C1）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public bool IsConsumed { get; set; }

    /// <summary>创建时间（UTC，CanUpdate=false）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
