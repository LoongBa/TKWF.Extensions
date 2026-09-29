using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Authentication
{
    /// <summary>
    /// 跨系统映射实体——平台内部 id ↔ 业务 app + 业务本地 id + UnionId（统一 DMP 双机制：映射表/外键）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// <para>DMP V4.0 管理员走映射表、会员走外键——Ext 以本表统一承载；DMP 迁移时会员映射迁入本表（需求 §6 约束 1）。</para>
    /// </summary>
    [Table("PlatformAccountMap")]
    [FreeSql.DataAnnotations.Index("UX_PlatformAccountMap", nameof(PlatformAccountId) + "," + nameof(BusinessAppId) + "," + nameof(BusinessLocalId), IsUnique = true)]
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

        /// <summary>创建时间（UTC）；唯一索引 (PlatformAccountId, BusinessAppId, BusinessLocalId)。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
