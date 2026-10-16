using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.TrustCenter
{
    /// <summary>
    /// OAuth 授权码实体（面② SSO 联邦流——拆分命名 2026-10-16，ADR109 决策 4 修订）。
    /// <para>自原 <see cref="AccessCodeEntity"/> 面②拆分：<c>authorization code → token2</c>
    /// （OAuth authorization code 语义，字段级消费）——独立 <c>IAuthorizationCodeService</c>。
    /// 只存 <c>SHA256(code)</c> 索引不存原文；TTL；<c>used</c> 原子 CAS 防重放；
    /// <c>code_verifier_hash</c> PKCE 可选（defense in depth）。</para>
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// </summary>
    [Table("TKWF_AuthorizationCode")]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthorizationCode_CodeHash", nameof(CodeHash), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuthorizationCodeEntity
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

        /// <summary>PKCE code_verifier SHA256 hash（可选——非 public client 非强制，Oracle P2-1）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(64)]
        public string? CodeVerifierHash { get; set; }

        /// <summary>消费 IP（审计）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        /// <summary>过期时间（UTC，TTL 受 TrustCenterOptions.AuthorizationCodeExpirationSeconds 配置）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime ExpiresAt { get; set; }

        /// <summary>单次消费标记（原子 CAS 置 true；重放即拒）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public bool Used { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}