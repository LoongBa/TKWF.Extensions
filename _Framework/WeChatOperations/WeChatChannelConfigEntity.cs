using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 公众号渠道配置实体（表 <c>TKWF_WeChatChannelConfig</c>）——基础配置（DB 权威，方案 §3.3.4 两源并存：DB 优先 → Options 降级）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>🔒 凭证安全（Oracle P1 安全五补充）：<see cref="AppSecretCipher"/>/<see cref="TokenCipher"/>/<see cref="EncodingAESKeyCipher"/>
    /// 三字段为 AES-GCM 单段密文（base64(nonce‖cipher‖tag)，对齐 AuthSurface V0.2.0 规范）——<b>明文永不落库</b>；
    /// DTO 投影不含凭证（密文不回显）；解密仅出站瞬时。</para>
    /// <para>唯一索引：ChannelId（公众号实例 id）/AppId（公众号 AppId——出站凭证解析键）。</para>
    /// </summary>
    [Table("TKWF_WeChatChannelConfig")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatChannelConfig_ChannelId", nameof(ChannelId), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatChannelConfig_AppId", nameof(AppId), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class WeChatChannelConfigEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>channel 实例 id（公众号实例 id——ISsoChannel.ChannelId 选区依据，Alipay 式双键内键）；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>公众号 AppId（出站 API 凭证明文解析键——WeChatApiClient 按 AppId 精确匹配）；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(64)]
        public string AppId { get; set; } = "";

        /// <summary>公众号 AppSecret（AES-GCM 单段密文——明文不落库，方案 §3.3.1 Oracle P1）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(512)]
        public string AppSecretCipher { get; set; } = "";

        /// <summary>公众号令牌 Token（事件推送验签 msg_signature 用——AES-GCM 单段密文；明文不落库）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(512)]
        public string TokenCipher { get; set; } = "";

        /// <summary>消息加解密密钥 EncodingAESKey（事件推送 AES 解密 43 字符——AES-GCM 单段密文；明文不落库）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(512)]
        public string EncodingAESKeyCipher { get; set; } = "";

        /// <summary>显示名称（运营侧可读名）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(100)]
        public string DisplayName { get; set; } = "";

        /// <summary>备注。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(500)]
        public string? Remark { get; set; }

        /// <summary>启用（默认 true；禁用 → 通道不选区）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>通道类型（平台库通道类型，如 wechat_oauth / wechat_event——IChannelSource 投影 ChannelType）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(50)]
        public string ChannelType { get; set; } = "";

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}