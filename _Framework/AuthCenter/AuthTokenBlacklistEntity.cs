using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 令牌黑名单实体——持久化黑名单（摒弃 DMP 内存 ConcurrentDictionary：重启丢失/多实例不一致）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// <para>条目 TTL = 该 token 自然过期时间；验签时查 jti 命中 → 拒绝（经 IMemoryCache 短 TTL 前置过滤）。</para>
    /// </summary>
    [Table("AuthTokenBlacklist")]
    [FreeSql.DataAnnotations.Index("UX_AuthTokenBlacklist_Jti", nameof(Jti), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuthTokenBlacklistEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>被撤销 Access Token 的 jti；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string Jti { get; set; } = "";

        /// <summary>平台内部 id。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(32)]
        public string UserId { get; set; } = "";

        /// <summary>黑名单条目过期（= 该 token 自然过期时间，UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public DateTime ExpiresAt { get; set; }

        /// <summary>撤销时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTime RevokedAt { get; set; } = DateTime.UtcNow;

        /// <summary>撤销原因：logout/revoke/security。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(50)]
        public string? Reason { get; set; }
    }
}
