using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 订阅统计实体（表 <c>TKWF_WeChatSubscriptionStat</c>）——按 channel 按日按来源统计订阅/取关（subscribe/unsubscribe 事件驱动，每日 upsert）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>唯一约束 (ChannelId, StatDate, Source)：每日每 channel 每来源一行——<b>汇总行 Source=null</b> + 各来源行
    /// （V0.3.0 来源/场景维度——DMP-Lite 5.7 搜索/扫码/二维码/名片/分享卡片/小程序筛选；
    /// <see cref="WeChatSubscriptionSources"/> 常量类）；<c>ISubscriptionStatService.RecordAsync</c> 按此键每日 upsert 原子累加。</para>
    /// </summary>
    [Table("TKWF_WeChatSubscriptionStat")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatSubscriptionStat_Channel_Date_Source",
        nameof(ChannelId) + "," + nameof(StatDate) + "," + nameof(Source), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class SubscriptionStatEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属 channel 实例 id。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>统计日期（按日粒度；唯一约束 (ChannelId, StatDate, Source)）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public DateTime StatDate { get; set; }

        /// <summary>来源/场景（<see cref="WeChatSubscriptionSources"/>——search/scan/qr_code/card/share/miniprogram；
        /// <b>空 = 汇总行</b>；唯一约束参与键 (ChannelId, StatDate, Source)——V0.3.0 来源维度）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(50)]
        public string? Source { get; set; }

        /// <summary>当日订阅数（subscribe 事件 +1）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public int SubscribeCount { get; set; }

        /// <summary>当日取关数（unsubscribe 事件 +1）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public int UnsubscribeCount { get; set; }

        /// <summary>当日净增（= SubscribeCount - UnsubscribeCount，冗余列方便趋势查询）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public int NetCount { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}