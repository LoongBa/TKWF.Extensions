using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Authentication
{
    /// <summary>
    /// 第三方平台凭证实体——公众号/小程序/网站应用/移动应用凭证（AppSecret AES-GCM 加密存储）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// <para>摒弃 DMP 纯 POCO 裸 FreeSql——SG1 化 + DataService 委托；加密在 DataService 边界（Service 层只见明文）。</para>
    /// </summary>
    [Table("PlatformCredential")]
    [FreeSql.DataAnnotations.Index("UX_PlatformCredential", nameof(Platform) + "," + nameof(AppType) + "," + nameof(AppId), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class PlatformCredentialEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台：wechat/douyin/alipay。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(20)]
        public string Platform { get; set; } = "";

        /// <summary>应用类型：公众号/小程序/网站应用/移动应用。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(20)]
        public string AppType { get; set; } = "";

        /// <summary>应用 AppId。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string AppId { get; set; } = "";

        /// <summary>应用名称（可选）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(128)]
        public string? AppName { get; set; }

        /// <summary>AppSecret AES-GCM 加密存储（AuthCenterOptions.SecretEncryptionKeyPath 派生密钥；DB 无明文）。
        /// 敏感字段：Dto 忽略 + Json 忽略（tkwf-entity 规则 4）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(-1)]
        [DtoFieldIgnore]
        [JsonIgnore]
        public string AppSecretEncrypted { get; set; } = "";

        /// <summary>是否启用（默认 true）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（UTC）；唯一索引 (Platform, AppType, AppId)。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
