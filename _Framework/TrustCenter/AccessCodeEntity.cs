using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.TrustCenter
{
    /// <summary>
    /// 授权码实体（accesscode，安全数据投递通道）——单次 + SHA256 存储 + 原子 CAS + PKCE 可选。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// <para>设计文档 §6.1 + Oracle P2-1/P2-2：只存 <c>SHA256(code)</c> 索引不存原文；TTL；
    /// <c>used</c> 原子 CAS（<c>UPDATE ... WHERE used=false</c>）防重放；<c>code_verifier_hash</c> PKCE 可选
    /// （defense in depth——复用 IOAuthTicketService 既有 PKCE 资产语义，Oracle P2-1）。</para>
    /// <para><b>TrustCenter 剥离（2026-10-09，方案 §5.8）</b>：<c>SsoAccessCodeEntity</c> → <see cref="AccessCodeEntity"/>
    /// （去 Sso 前缀——语义泛化为数据投递，非 SSO 专属）；表名 <c>TKWF_SsoAccessCode</c> → <c>TKWF_AccessCode</c>
    /// （不撞名不简称——TrustCenter 扩展内部实体，仅加默认前缀）；命名空间 TKWF.Ext.TrustCenter。</para>
    /// </summary>
    [Table("TKWF_AccessCode")]
    [FreeSql.DataAnnotations.Index("TKWFIX_AccessCode_CodeHash", nameof(CodeHash), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AccessCodeEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>SHA256(code) hex——只存 hash 不存原文（防库泄露后 code 被盗用）；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string CodeHash { get; set; } = "";

        /// <summary>channel 实例 id（微信 = 公众号 id）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(64)]
        public string ChannelId { get; set; } = "";

        /// <summary>目标应用 id（token2 <c>aud</c>）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(64)]
        public string TargetAppId { get; set; } = "";

        /// <summary>平台内部 id（消费后签 token2 <c>sub</c>）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(32)]
        public string UId { get; set; } = "";

        /// <summary>该应用注册范围（签发 token2 时注入 scope）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(128)]
        public string? Scope { get; set; }

        /// <summary>过期时间（UTC，TTL 默认 120 秒）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime ExpiresAt { get; set; }

        /// <summary>单次消费标记（原子 CAS 置 true；重放即拒）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool Used { get; set; }

        /// <summary>消费 IP（审计）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        /// <summary>PKCE code_verifier SHA256 hash（可选——非 public client 非强制，Oracle P2-1）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(64)]
        public string? CodeVerifierHash { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;

        /// <summary>附带信息 AES-GCM 密文（安全数据投递通道——方案 §5.5；base64(nonce[12]‖cipher‖tag[16]) 单段）。
        /// <para>明文不落库；null = 纯核销通道（无附带信息）。容量：列 MaxLength(4096)，明文最大 ~4068 字节
        /// （重载 <c>IssueAsync(payloadJson,...)</c> 入口校验超限抛 PAYLOAD_TOO_LARGE）。</para>
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        [MaxLength(4096)]
        public string? PayloadEncrypted { get; set; }

        /// <summary>可核销人标识（expected claimant——方案 §5.5：谁可核销；null = 可转让，任意核销人可消费）。
        /// <para>进原子 CAS 条件（<c>UPDATE ... WHERE ... (ExpectedClaimant IS NULL OR ExpectedClaimant = ?)</c>）——
        /// 无 TOCTOU，核销人与并发核销联合判定恰一成功。</para>
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 14)]
        [MaxLength(64)]
        public string? ExpectedClaimant { get; set; }
    }
}
