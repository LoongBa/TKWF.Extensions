using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 下游转发端点实体（表 <c>TKWF_WeChatDownstreamEndpoint</c>）——网关消息转发下游端点（HTTP 出站目标，V0.2.0 增网关模块）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para><see cref="BaseUrl"/> + <see cref="Path"/>（可空）拼接出站目标；<see cref="HttpMethod"/> 默认 "POST"（微信被动回复/转发语义）。</para>
    /// <para><see cref="AuthType"/>（int）：0=None 无鉴权 / 1=Bearer 头携带 / 2=Header 自定义头携带。
    /// <see cref="AuthTokenCipher"/>：AES-GCM 单段密文（base64(nonce‖cipher‖tag)，对齐 <see cref="WeChatChannelConfigEntity"/> 凭证密文列）——
    /// <b>明文永不落库</b>；解密仅出站瞬时。</para>
    /// <para>唯一索引：<see cref="ChannelId"/> + <see cref="Name"/>（channel 内端点名唯一——按名选区/去重）。</para>
    /// </summary>
    [Table("TKWF_WeChatDownstreamEndpoint")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatDownstreamEndpoint_Channel_Name", nameof(ChannelId) + "," + nameof(Name), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class DownstreamEndpointEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属 channel 实例 id（多公众号数据隔离——端点读一律按 ChannelId 过滤）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>端点名称（channel 内唯一——唯一索引 (ChannelId, Name)，按名选区/去重）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        /// <summary>基础 URL（出站目标主机——BaseUrl + Path 拼接完整目标）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(500)]
        public string BaseUrl { get; set; } = "";

        /// <summary>路径（可空——缺省直接命中 BaseUrl）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(200)]
        public string? Path { get; set; }

        /// <summary>HTTP 方法（默认 "POST"——微信转发语义为 POST 透传）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(10)]
        public string HttpMethod { get; set; } = "POST";

        /// <summary>超时秒数（默认 5——出站等待上游响应的上限）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public int TimeoutSeconds { get; set; } = 5;

        /// <summary>启用（默认 true；禁用 → 路由不选区该端点）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>鉴权类型（int——0=None 无鉴权 / 1=Bearer 头携带 / 2=Header 自定义头携带）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public int AuthType { get; set; }

        /// <summary>鉴权令牌（AES-GCM 单段密文——明文不落库，方案对齐 WeChatChannelConfigEntity 凭证密文列）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(512)]
        public string AuthTokenCipher { get; set; } = "";

        /// <summary>失败重试次数（可空——null 不重试）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public int? RetryCount { get; set; }

        /// <summary>备注。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        [MaxLength(500)]
        public string? Remark { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 14)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
