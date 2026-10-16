using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.TrustCenter
{
    /// <summary>
    /// 密封票据实体（面① 安全数据投递/密封票据——拆分命名 2026-10-16，ADR109 决策 4 修订）。
    /// <para>自原 <c>AccessCodeEntity</c> 面①拆分：载荷密封 + 单次兑换 + TTL + 可核销人——
    /// 并入框架 <c>ISealedTicketService&lt;T&gt;</c> 的 DB CAS 后端（<c>SealedTicketService&lt;T&gt;</c>）。
    /// 只存 <c>SHA256(code)</c> 索引不存原文；<c>used</c> 原子 CAS 防重放；
    /// <c>ExpectedClaimant</c> 可核销人进 CAS 条件（无 TOCTOU）。</para>
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// </summary>
    [Table("TKWF_SealedTicket")]
    [FreeSql.DataAnnotations.Index("TKWFIX_SealedTicket_CodeHash", nameof(CodeHash), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class SealedTicketEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>SHA256(ticket) hex——只存 hash 不存原文（防库泄露后 ticket 被盗用）；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string CodeHash { get; set; } = "";

        /// <summary>可核销人标识（expected claimant——谁可核销；null = 可转让，任意核销人可消费）。
        /// <para>进原子 CAS 条件——无 TOCTOU，核销人与并发核销联合判定恰一成功。</para>
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(64)]
        public string? ExpectedClaimant { get; set; }

        /// <summary>载荷 AES-GCM 密文（安全数据投递通道——方案 §5.5；base64(nonce[12] + cipher + tag[16]) 单段）。
        /// <para>明文不落库；null = 纯核销通道（无附带信息）。容量：列 MaxLength(4096)，明文最大 4068 字节
        /// （签发入口校验超限抛 PAYLOAD_TOO_LARGE）。</para>
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(4096)]
        public string? PayloadEncrypted { get; set; }

        /// <summary>过期时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTime ExpiresAt { get; set; }

        /// <summary>单次消费标记（原子 CAS 置 true；重放即拒）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public bool Used { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}