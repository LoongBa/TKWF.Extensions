using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 刷新令牌实体——Refresh rotation（SHA256 落库，新旧不可复用 + TokenVersion 闭环）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// <para>令牌明文不落库——仅存 SHA256 hex；刷新时校验 TokenVersion 匹配（不匹配 → 拒绝，闭环 DMP 缺口）。</para>
    /// </summary>
    [Table("AuthRefreshToken")]
    [FreeSql.DataAnnotations.Index("UX_AuthRefreshToken_TokenHash", nameof(TokenHash), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("IX_AuthRefreshToken_UserVersion", nameof(UserId) + "," + nameof(TokenVersion))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuthRefreshTokenEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>对应 Access Token jti。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string Jti { get; set; } = "";

        /// <summary>平台内部 id（JWT sub 引用）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(32)]
        public string UserId { get; set; } = "";

        /// <summary>签发时认证方式：sms/wechat/password/redeem。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(20)]
        public string AuthType { get; set; } = "";

        /// <summary>Refresh Token SHA256 hex（不存明文）；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(64)]
        public string TokenHash { get; set; } = "";

        /// <summary>签发时账号 TokenVersion——刷新时校验（不匹配 → 拒绝）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public int TokenVersion { get; set; }

        /// <summary>过期时间（UTC，默认 30d）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime ExpiresAt { get; set; }

        /// <summary>是否已撤销（rotation 消费后置 true）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool IsRevoked { get; set; }

        /// <summary>撤销时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime? RevokedAt { get; set; }

        /// <summary>签发设备信息（可选）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(200)]
        public string? DeviceInfo { get; set; }

        /// <summary>创建时间（UTC）；索引 (UserId, TokenVersion)。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}
