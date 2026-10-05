using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 一次性票据实体——票据换令牌（TTL 5min 单次消费 + PKCE + app_id/redirect_uri 白名单 + state 防重放）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// <para>回调承载铁律（用户裁定 + Oracle B1）：URL 只带一次性票据 + redirect_uri，绝不带敏感信息；纯前端静态站走公网 /oauth/exchange + PKCE code_verifier。</para>
    /// </summary>
    [Table("OAuthTicket")]
    [FreeSql.DataAnnotations.Index("UX_OAuthTicket_Ticket", nameof(Ticket), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class OAuthTicketEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>高熵随机票据（防枚举）；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string Ticket { get; set; } = "";

        /// <summary>票据类型：login/bind（登录 vs 绑定隔离——Oracle I6 防绑定劫持）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(20)]
        public string TicketType { get; set; } = "login";

        /// <summary>目标应用 id（换取校验——防跨应用抢先消费）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(64)]
        public string AppId { get; set; } = "";

        /// <summary>重定向 URI（白名单校验）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(512)]
        public string RedirectUri { get; set; } = "";

        /// <summary>CSRF state（登录 vs 绑定隔离）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(128)]
        public string? State { get; set; }

        /// <summary>PKCE：SHA256(code_verifier)（静态站公网换令牌——Oracle B1）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(64)]
        public string? CodeVerifierHash { get; set; }

        /// <summary>签发时绑定用户（登录成功/绑定归并后）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(32)]
        public string? UserId { get; set; }

        /// <summary>过期时间（UTC，TTL 5min）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime ExpiresAt { get; set; }

        /// <summary>是否已消费（单次消费）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public bool IsConsumed { get; set; }

        /// <summary>消费时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime? ConsumedAt { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}
