using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 跨系统映射实体——平台内部 id ↔ 业务 app + 业务本地 id + UnionId（统一 DMP 双机制：映射表/外键）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// <para>DMP V4.0 管理员走映射表、会员走外键——Ext 以本表统一承载；DMP 迁移时会员映射迁入本表（需求 §6 约束 1）。</para>
    /// </summary>
    [Table("TKWF_PlatformAccountMap")]
    [FreeSql.DataAnnotations.Index("TKWFIX_PlatformAccountMap", nameof(PlatformAccountId) + "," + nameof(BusinessAppId) + "," + nameof(BusinessLocalId), IsUnique = true)]
    // SSO 联邦映射（channel_id + external_user_id → uid）专用唯一索引——既有 UX 保留不变（V0.5.x 消费方零回归），
    // 新索引对 NULL 放行（既有行 ChannelId/ExternalUserId 为空不冲突，ADR-SSO Oracle P1-3）
    [FreeSql.DataAnnotations.Index("TKWFIX_PlatformAccountMap_Channel", nameof(ChannelId) + "," + nameof(ExternalUserId), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class PlatformAccountMapEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台内部 id（Ext 账号表 AuthAccount.UId）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(32)]
        public string PlatformAccountId { get; set; } = "";

        /// <summary>业务应用 id。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(64)]
        public string BusinessAppId { get; set; } = "";

        /// <summary>业务系统本地 id。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string BusinessLocalId { get; set; } = "";

        /// <summary>微信 unionid（可选，跨应用关联）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(64)]
        public string? UnionId { get; set; }

        /// <summary>channel 实例 id（SSO 联邦映射，如微信公众号 id；可空——既有业务 app 映射行不填，ADR-SSO Oracle P1-3）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(64)]
        public string? ChannelId { get; set; }

        /// <summary>渠道身份标识（SSO 联邦映射，如商户 openid2；可空——既有行 NULL 放行）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(128)]
        public string? ExternalUserId { get; set; }

        /// <summary>创建时间（UTC）；唯一索引 (PlatformAccountId, BusinessAppId, BusinessLocalId)。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
