using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.TrustCenter
{
    /// <summary>
    /// SSO 应用注册实体（下游 SP 接入点）——app_id + origin 白名单 + scope + client credential + per-channel HMAC 密钥。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para>设计文档 §5.4/§6.3：ClientSecret 与 HmacSecret 均 AES-GCM 加密（DataService 边界加解密——
    /// 对齐 PlatformCredentialEntity 先例）；<c>HmacSecretEncrypted</c> 为 per-channel + per-app HMAC 密钥
    /// （<c>/sso/issue</c> 验签密钥来源，Oracle P1-4——不复用 PlatformCredentialEntity）。</para>
    /// <para><b>TrustCenter 剥离（2026-10-09）</b>：自 Federation 迁入——命名空间 TKWF.Ext.TrustCenter；
    /// 实体名 SsoClientEntity 保留（方案 §5.8——SSO/信任接入语义仍准确）；表名 TKWF_SsoClient 保留。</para>
    /// </summary>
    [Table("TKWF_SsoClient")]
    [FreeSql.DataAnnotations.Index("TKWFIX_SsoClient_AppId", nameof(AppId), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class SsoClientEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>目标应用 id（token2 <c>aud</c>）；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string AppId { get; set; } = "";

        /// <summary>注册精确 origin（scheme+host）集合 JSON——防开放重定向（设计文档 §8.4：不接受自由 redirect_uri 参数）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(512)]
        public string OriginWhitelist { get; set; } = "[]";

        /// <summary>该应用注册范围 JSON（默认 <c>["profile:basic"]</c>）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(256)]
        public string Scopes { get; set; } = "[\"profile:basic\"]";

        /// <summary>app client credential（AES-GCM 密文——DataService 边界加解密，对齐 PlatformCredential 先例）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(512)]
        public string ClientSecretEncrypted { get; set; } = "";

        /// <summary>per-channel + per-app HMAC 密钥（AES-GCM 密文）——<c>/sso/issue</c> 验签密钥来源（Oracle P1-4）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(512)]
        public string HmacSecretEncrypted { get; set; } = "";

        /// <summary>启用（默认 true；禁用 → 登录/消费拒绝 403）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
