using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Federation
{
    /// <summary>
    /// 通道注册表实体（多通道联邦 Phase 2——DB 动态权威层，方案 docs/Federation/多通道联邦-开发方案.md §3.3）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para>凭证模型（方案 §3.1 前瞻核心）：公共列（<see cref="ChannelId"/>/<see cref="PlatformType"/>/<see cref="AppId"/>+
    /// <see cref="AppSecretEncrypted"/>）+ 扩展 JSON 密文列（<see cref="ExtraJsonEncrypted"/>——平台特定负载 AES-GCM
    /// 整段加密：WeChat{Token,EncodingAESKey} / OIDC{TokenIssuers,DiscoveryUri} / 支付宝（未来）{AppPrivateKey}——
    /// 未来平台零表结构变更）。公共列 <see cref="AppId"/> 仅承载 OAuth 握手应用标识（Oracle M7 映射规则约束），
    /// 法人级标识（CorpId/OrgId/AgentId 类）一律进 ExtraJson。</para>
    /// <para>加密语义（方案 M6/M12）：<see cref="AppSecretEncrypted"/> 与 <see cref="ExtraJsonEncrypted"/> 均
    /// AES-GCM 密文列（keyed <c>ISymmetricKeyProvider "Federation"</c>），服务层加解密（<c>SsoChannelRegistryService</c>）——
    /// 生产用 DB 层全程密文，明文不落库（静态 Options 明文仅限开发态，方案 M12）。</para>
    /// <para>对外别名（Phase 3，方案 §3.7 双键）：<see cref="ChannelAlias"/> 列 = 通道实例对外名（可空，缺省
    /// null = 对外用 <see cref="ChannelId"/>）；非唯一索引（兼容 alias=ChannelId 同名迁移），解析统一经
    /// <c>IChannelRegistry.GetByAliasOrIdAsync</c>（先 alias → 再 ChannelId）。</para>
    /// <para>容量核验（Oracle M6）：<see cref="ExtraJsonEncrypted"/>(4000) 起步——RSA-2048 PEM 私钥约 1.7KB +
    /// OIDC DiscoveryUri+TokenIssuers 组合 1.5KB+ 加密 base64 膨胀 ~33%（P1 定案锁定，避免 P2 迁移）；
    /// <see cref="AppSecretEncrypted"/>(512) 承载对称密钥/AES-GCM 密文足量。</para>
    /// </summary>
    [Table("TKWF_SsoChannelRegistry")]
    [FreeSql.DataAnnotations.Index("TKWFIX_SsoChannelRegistry_ChannelId", nameof(ChannelId), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class SsoChannelRegistryEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>通道实例键（<c>ISsoChannel.ChannelId</c> 选区依据——如公众号 id）；唯一索引（UX——动态权威层精确选区）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string ChannelId { get; set; } = "";

        /// <summary>平台族字符串（wechat / qq / dingtalk / wecom / oidc / google / microsoft / alipay...开放注册表，不枚举）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(32)]
        public string PlatformType { get; set; } = "";

        /// <summary>
        /// 通道对外别名（方案 §3.7 双键——可空，缺省 null = 对外用 <see cref="ChannelId"/>，存量零迁移）。
        /// <para>语义：alias = 通道实例的<b>对外名</b>（URL 路径段 / 展示 / 客户端入参），与内部
        /// <see cref="ChannelId"/> 一一对应；可读直观 + 保护内部持久化键。<b>非唯一索引</b>（P3-3 明确不加 UX——
        /// 兼容 alias=ChannelId 同名迁移：存量行 channelId 即对外名，新 alias 可与之同名；解析优先级
        /// alias 精确匹配 → ChannelId 匹配，冲突由注册门面校验 fail-closed）。</para>
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(64)]
        public string? ChannelAlias { get; set; }

        /// <summary>OAuth 握手应用标识（AppId / ClientId 归一——OIDC 系 = client_id）；法人级标识进 ExtraJson（M7）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(128)]
        public string? AppId { get; set; }

        /// <summary>对称应用密钥（AES-GCM 密文——AppSecret 明文不落库；OIDC 系 = ClientSecret）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(512)]
        public string? AppSecretEncrypted { get; set; }

        /// <summary>扩展 JSON 密文（AES-GCM 整段加密——平台特定负载 ExtraJson，容量核验 4000 起步 / Oracle M6）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(4000)]
        public string? ExtraJsonEncrypted { get; set; }

        /// <summary>默认通道标记（单通道降级（活跃数==1；多通道 B 守卫 CHANNEL_REQUIRED）选区；多公众号语义边界见方案 §3.2）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool IsDefault { get; set; }

        /// <summary>启停标记（默认 true；禁用 → CHANNEL_DISABLED）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}