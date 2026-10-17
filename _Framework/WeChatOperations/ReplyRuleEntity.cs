using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 回复规则实体（表 <c>TKWF_WeChatReplyRule</c>）——通用回复规则表（Oracle P1 泛化：原 KeywordRule/ClickReplyMap/WelcomeConfig 合并）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>SceneType（int）：1=Subscribe 欢迎语 / 2=Keyword 关键词 / 3=Click 菜单 key 映射 / 4=Image / 5=Voice / 6=Video /
    /// 7=ShortVideo / 8=Location / 9=Link / 10=Scan 扫码 / 11=Default 兜底。</para>
    /// <para><see cref="TriggerKey"/>：关键词（Keyword）或菜单 key（Click）——场景型规则（Subscribe/Image/.../Scan/Default）为空。
    /// <see cref="MatchMode"/> 仅 Keyword 场景适用（1=Exact 完全匹配 / 2=Contains 包含匹配）。
    /// <see cref="ReplyText"/>（直接文本）优先于 <see cref="MaterialId"/>（素材引用）——存在 ReplyText 用 ReplyText，否则走素材。</para>
    /// <para>⚠️ Click 映射独立于菜单结构：SceneType=Click 的规则修改<b>不触发</b> menu/create（Oracle P1 语义保留）。</para>
    /// </summary>
    [Table("TKWF_WeChatReplyRule")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatReplyRule_Channel_Scene", nameof(ChannelId) + "," + nameof(SceneType))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class ReplyRuleEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属 channel 实例 id（多公众号数据隔离——规则读一律按 ChannelId 过滤）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>触发场景（int——1 Subscribe / 2 Keyword / 3 Click / 4 Image / 5 Voice / 6 Video / 7 ShortVideo / 8 Location / 9 Link / 10 Scan / 11 Default）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public int SceneType { get; set; }

        /// <summary>触发键（关键词或菜单 key——场景型规则为空；Click 场景 = 菜单按钮 key）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(200)]
        public string? TriggerKey { get; set; }

        /// <summary>匹配模式（int——仅 Keyword 适用：1=Exact 完全匹配 / 2=Contains 包含匹配；其余场景 0）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public int MatchMode { get; set; }

        /// <summary>素材引用（<see cref="MaterialEntity"/> 全局共享——可空；存在 ReplyText 时优先走直接文本）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public long? MaterialId { get; set; }

        /// <summary>直接回复文本（优先于素材——非空时忽略 MaterialId）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(2000)]
        public string? ReplyText { get; set; }

        /// <summary>启用（默认 true；禁用 → 规则跳过）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>排序（同场景内小在前——多规则时按序尝试）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public int SortOrder { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}