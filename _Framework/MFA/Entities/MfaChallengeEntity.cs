using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA 挑战票据实体——挑战-验证流状态承载（一次性 + TTL + 单次消费）。
/// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；保留 BCL <c>[Table("TKWF_MfaChallenge")]</c>；
/// 列映射用 FreeSql <c>[Column]</c>（全限定）。</para>
/// <para>挑战票据模型（ADR-MFA-挑战票据与验证模型 定案）：SMS 场景 <c>CodeHash</c> 落 SHA256 码（明文不落库）；
/// TOTP 场景 <c>CodeHash</c> null（无状态验证——票据仅作流程句柄 + 频控挂点 + 审计）。</para>
/// <para>单次消费：<c>IsConsumed</c> 原子翻转（成功验证后同票据/同码拒绝——防重放）；TTL <c>ExpireAt</c>（5min 对齐 OAuthTicket）。
/// 尝试频控归内存滑动窗口（per-user×method，Oracle C4——本实体不设 Attempts 列）。</para>
/// <para>索引：<c>IX_MfaChallenge_User_Method</c>（UserId+Method——按用户方法查活动挑战）+ <c>IX_MfaChallenge_Expire</c>（过期清理）。</para>
/// </summary>
[Table("TKWF_MfaChallenge")]
[FreeSql.DataAnnotations.Index("TKWFIX_MfaChallenge_User_Method", nameof(UserId) + "," + nameof(Method))]
[FreeSql.DataAnnotations.Index("TKWFIX_MfaChallenge_Expire", nameof(ExpireAt))]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class MfaChallengeEntity
{
    /// <summary>主键（自增——即 challengeId 句柄）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>归属用户 Id。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(128)]
    public string UserId { get; set; } = "";

    /// <summary>MFA 方法（"totp" / "sms"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(20)]
    public string Method { get; set; } = "";

    /// <summary>SMS 码 SHA256（仅 SMS 场景；TOTP 场景 null——无码落库；写入后不可变）。
    /// 敏感字段：Dto 忽略 + Json 忽略（tkwf-entity 规则 4——哈希泄露允许离线字典/挑战状态探测）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true, CanUpdate = false)]
    [MaxLength(64)]
    [DtoFieldIgnore]
    [JsonIgnore]
    public string? CodeHash { get; set; }

    /// <summary>挑战过期时间（TTL 5min——过期后验证拒绝）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public DateTime ExpireAt { get; set; }

    /// <summary>是否已消费（成功验证后翻转 true——单次消费防重放，Oracle C1）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public bool IsConsumed { get; set; }

    /// <summary>挑战创建时间（UTC，CanUpdate=false）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
