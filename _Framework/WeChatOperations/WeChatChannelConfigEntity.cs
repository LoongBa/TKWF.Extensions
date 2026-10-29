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
    /// <para>唯一索引：ChannelId（公众号实例 id——不变键）/AppId（公众号 AppId——出站凭证解析键）/
    /// <b>D03 1.2 新增</b>：(ChannelForm, ChannelId) 复合唯一（D03 §4.1.3——通道形态 × ChannelId；
    /// ChannelId 全局唯一为微信事实，复合唯一防跨形态撞 id）+ Alias 全局唯一（可空——null 不参与唯一，
    /// 无别名通道多行共存；1.6 alias 规则：小写 slug 不可变）。</para>
    /// <para>⚠️ <see cref="ChannelType"/> 是<b>协议通道类型</b>（wechat_oauth/wechat_event——平台库 ISsoChannel 选区）；
    /// <see cref="ChannelForm"/> 是<b>通道形态</b>（D03 ChannelType 语义：mp_subscription/mp_service/mini_program——
    /// 二者语义正交（服务号同时有 OAuth+Event 两协议通道），勿混用（Oracle6 条件 4 命名碰撞警示）。</para>
    /// </summary>
    [Table("TKWF_WeChatChannelConfig")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatChannelConfig_ChannelId", nameof(ChannelId), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatChannelConfig_AppId", nameof(AppId), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatChannelConfig_Form_ChannelId", nameof(ChannelForm) + "," + nameof(ChannelId), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatChannelConfig_Alias", nameof(Alias), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class WeChatChannelConfigEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>channel 实例 id（公众号实例 id——ISsoChannel.ChannelId 选区依据，Alipay 式双键内键；不变键）；唯一索引。</summary>
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

        /// <summary>显示名称（运营侧可读名——可变展示名，A14：不入票）。</summary>
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

        /// <summary>协议通道类型（wechat_oauth / wechat_event——平台库 ISsoChannel.ChannelType 选区依据；<b>非 D03 通道形态</b>）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(50)]
        public string ChannelType { get; set; } = "";

        /// <summary>
        /// 通道形态（D03 §4.1 ChannelType 语义——mp_subscription / mp_service / mini_program / work_wechat；
        /// 接入时定、稳定一等字段）。<b>与 <see cref="ChannelType"/>（协议类型）正交</b>：
        /// 服务号（mp_service）可同时有 wechat_oauth + wechat_event 两协议通道。
        /// 存量迁移默认回填 <c>mp_service</c>（生产 DBA 脚本；D03 1.2 新增列）。
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        [MaxLength(50)]
        public string ChannelForm { get; set; } = "mp_service";

        /// <summary>
        /// 认证状态（D03 §4.1 Verification——<c>none</c> / <c>verified</c>；<b>人工配置字段</b>——
        /// 微信无认证状态查询 API，R10；与 ChannelForm 联合派生 Capability（1.2）。
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        [MaxLength(20)]
        public string Verification { get; set; } = "none";

        /// <summary>
        /// 通道对外别名（A14 定稿——<b>不可变 slug</b>：小写字母数字连字符 <c>^[a-z0-9][a-z0-9-]{2,63}$</c>；
        /// 全局唯一（null 不参与唯一——无别名通道多行共存）；写入守卫见 ChannelConfigService（1.6）；
        /// 可变展示名走 <see cref="DisplayName"/>（不入票）。
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        [MaxLength(64)]
        public string? Alias { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 14)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 15)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}