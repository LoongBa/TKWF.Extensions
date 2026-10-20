using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Federation
{
    /// <summary>
    /// 关注/取关事件事实表（I2 渠道关注关系域——追加写，事实不可丢：事件行无论是否排序更新都落库）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例；
    /// 对齐 <see cref="SsoChannelRegistryEntity"/> 体例）。</para>
    /// <para>幂等（方案 §4.3）：<see cref="DedupeKey"/> = <c>{channelId}|{eventType}|{externalUserId}|{occurredAtUnix}|{eventKey}|{messageId}</c>
    /// （超 256 字符 → SHA256 十六进制——保留可判定性，截断会产生假碰撞）；微信事件无 MsgId，CreateTime 秒级 + FromUserName + Event
    /// 已足够唯一，重复推送必然同键。</para>
    /// <para><see cref="Source"/>（R5）对齐 WeChatOperations 统计维度（扫码来源/渠道标识）；<see cref="EventType"/>
    /// = follow / unfollow / scan_follow / scan_only / message / menu_click（平台厂商语义归一）。</para>
    /// </summary>
    [Table("TKWF_FollowEvent")]
    [FreeSql.DataAnnotations.Index("TKWFIX_FollowEvent_Dedupe", "DedupeKey", IsUnique = true)]
    [FreeSql.DataAnnotations.Index("TKWFIX_FollowEvent_Channel_External_Occurred", "ChannelId,ExternalUserId,OccurredAt")]
    [FreeSql.DataAnnotations.Index("TKWFIX_FollowEvent_Channel_Occurred", "ChannelId,OccurredAt")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class FollowEventEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>渠道实例（公众号 id——对齐 ISsoChannel.ChannelId）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string ChannelId { get; set; } = "";

        /// <summary>渠道身份标识（openid / userid）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string ExternalUserId { get; set; } = "";

        /// <summary>跨渠道关联 id（微信 UnionId——有则记，可空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string? UnionId { get; set; }

        /// <summary>事件类型（follow / unfollow / scan_follow / scan_only / message / menu_click——平台语义归一）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(32)]
        public string EventType { get; set; } = "";

        /// <summary>附加键（扫码 scene / 菜单 key——可空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(256)]
        public string? EventKey { get; set; }

        /// <summary>来源场景（R5——扫码来源/渠道标识，供统计维度；对齐 WeChatOperations 的 SubscriptionStat.Source）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(64)]
        public string? Source { get; set; }

        /// <summary>消息类事件的 MsgId（事件类为 null）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(64)]
        public string? MessageId { get; set; }

        /// <summary>渠道时间（UTC——排序守卫主判据）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime OccurredAt { get; set; }

        /// <summary>服务端接收（UTC——排序守卫同秒次判据）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTime ReceivedAt { get; set; }

        /// <summary>幂等键（UNIQUE——并发重投防双记；超 256 字符 SHA256）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        [MaxLength(256)]
        public string DedupeKey { get; set; } = "";

        /// <summary>落库时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}