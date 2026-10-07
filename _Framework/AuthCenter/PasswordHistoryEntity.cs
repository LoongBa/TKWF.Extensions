using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 密码历史实体（V0.9.0 ADR-密码策略与口令协议 决策 5——历史防重用 + 最近改密时间承载，只增表）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（ADR42 D4，供任意装配实例）。</para>
    /// <para><b>只增语义</b>：无 Update/Delete 公开业务方法（对齐 SecurityLog/只增不改先例）——改密落地时追加一条
    /// （ClientHash 组装格式 + Salt），保留最近 <c>PasswordPolicyOptions.HistoryRetentionCount</c> 代（清理策略由门面裁剪）。</para>
    /// <para>⚠️ <b>最近改密时间承载</b>（Oracle1 B2 修正）：<c>LastPasswordChangedAt</c> <b>不入</b> AuthAccount 列——
    /// 强制轮换判定查本表最新行 <see cref="CreateTime"/>（A.2 白名单：轮换默认关 ≠ 每个部署普遍需要，故不入账号列）。</para>
    /// <para>ClientHash 为客户端算的 <c>{iterations}.{b64salt}.{b64hash}</c> 组装格式（SecurePassword 协议——服务端零明文，
    /// ADR-密码策略与口令协议 决策 1；存历史仅作防重用比对，不用于登录验证——登录读 AuthAccount.PasswordHash 当前值）。</para>
    /// <para>表名 <c>TKWF_PasswordHistory</c>（ADR100——AuthCenter 表前缀批次）。</para>
    /// </summary>
    [Table("TKWF_PasswordHistory")]
    [FreeSql.DataAnnotations.Index("TKWFIX_PasswordHistory_UId", nameof(UId), IsUnique = false)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class PasswordHistoryEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台内部 id（AuthAccount.UId——历史归属账号）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(32)]
        public string UId { get; set; } = "";

        /// <summary>客户端算的密码散列组装格式（<c>{iterations}.{b64salt}.{b64hash}</c>——防重用比对源；服务端零明文）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(512)]
        public string ClientHash { get; set; } = "";

        /// <summary>创建时间（UTC——最近改密时间承载：强制轮换判定查最新行）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}