using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA 绑定实体——用户×方法绑定记录（启用 = 有已激活记录）。
/// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；保留 BCL <c>[Table("MfaSecret")]</c>；
/// 列映射用 FreeSql <c>[Column]</c>（全限定）。</para>
/// <para>双方法一实体（Oracle Q5 裁决）：TOTP 场景 <c>SecretEncrypted</c>（AES-GCM 密文——DataService 边界加解密，
/// 对齐 <c>PlatformCredentialKeyStore</c> 先例）；SMS 场景 <c>Phone</c>（绑手机）；另一列 null（列可空）。</para>
/// <para>绑定生命周期（ADR-MFA-挑战票据与验证模型 + Oracle C3）：<c>EnrollAsync</c> 写入（IsConfirmed=false +
/// EnrollTokenHash + EnrollExpireAt）→ <c>ConfirmEnrollAsync</c> 校验一次码后翻转 IsConfirmed + 清空 enrollToken；
/// 激活后 <c>SecretEncrypted</c>/<c>Phone</c> 不可改（CanUpdate=false——解绑重绑走 Disable+Enroll，ADR 定案）。</para>
/// <para>索引：<c>UX_MfaSecret_User_Method</c>（UserId+Method 联合唯一——每用户每方法一条绑定）。</para>
/// </summary>
[Table("MfaSecret")]
[FreeSql.DataAnnotations.Index("UX_MfaSecret_User_Method", nameof(UserId) + "," + nameof(Method), IsUnique = true)]
[FreeSql.DataAnnotations.Index("IX_MfaSecret_User", nameof(UserId))]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class MfaSecretEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>归属用户 Id（字符串——不限定数值型，对齐 MFA 独立于用户模型的设计；与 Method 联合唯一）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(128)]
    public string UserId { get; set; } = "";

    /// <summary>MFA 方法（"totp" / "sms"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(20)]
    public string Method { get; set; } = "";

    /// <summary>TOTP secret AES-GCM 密文（"base64(iv).base64(tag).base64(cipher)"——明文不落库；
    /// 仅 TOTP 场景；写入后不可变 CanUpdate=false——重新绑定走 Disable+Enroll）。
    /// 敏感字段：Dto 忽略 + Json 忽略（tkwf-entity 规则 4——对齐 PlatformCredentialEntity.AppSecretEncrypted 先例）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true, CanUpdate = false)]
    [MaxLength(512)]
    [DtoFieldIgnore]
    [JsonIgnore]
    public string? SecretEncrypted { get; set; }

    /// <summary>SMS 绑手机号（仅 SMS 场景；写入后不可变 CanUpdate=false——解绑重绑走 Disable+Enroll）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5, IsNullable = true, CanUpdate = false)]
    [MaxLength(32)]
    public string? Phone { get; set; }

    /// <summary>是否已激活（false = 绑定待激活——ConfirmEnroll 校验一次码后翻转 true；启用判定 = IsConfirmed）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public bool IsConfirmed { get; set; }

    /// <summary>绑定待激活令牌 SHA256（Oracle C3——EnrollToken 单次消费 + TTL；激活后清空）。
    /// 敏感字段：Dto 忽略 + Json 忽略（tkwf-entity 规则 4——哈希泄露允许离线字典/绑定状态探测）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7, IsNullable = true)]
    [MaxLength(64)]
    [DtoFieldIgnore]
    [JsonIgnore]
    public string? EnrollTokenHash { get; set; }

    /// <summary>绑定待激活令牌过期时间（Oracle C3——TTL 5min 对齐 ChallengeTtl）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8, IsNullable = true)]
    public DateTime? EnrollExpireAt { get; set; }

    /// <summary>创建时间（UTC，CanUpdate=false）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>更新时间（UTC——激活翻转时更新）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}
