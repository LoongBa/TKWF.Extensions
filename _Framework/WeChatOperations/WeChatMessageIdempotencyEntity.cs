using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 消息幂等实体（表 <c>TKWF_WeChatMessageIdempotency</c>）——网关消息幂等键（防止同一微信消息重复转发/重复回复，V0.2.0 增网关模块）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para><see cref="IdempotencyKey"/>：规范化幂等键（ChannelId+MsgId+FromUserName+CreateTime 规范化拼接——
    /// 去空白/统一大小写后哈希或定长拼串，语义唯一）。</para>
    /// <para><see cref="CreateTime"/>：微信原始消息时间（<b>原样留存，非 UTC 转换</b>——微信时间戳语义保留）；
    /// <see cref="FirstSeenAt"/>：首次见到该键时间（UTC）。</para>
    /// <para><see cref="LastStatus"/>（int）：1=Pending 处理中 / 2=Forwarded 已转发 / 3=Skipped 已跳过（重复/无规则）。</para>
    /// <para>唯一索引：<see cref="IdempotencyKey"/>——同键二次入站幂等命中（防微信重推）。</para>
    /// </summary>
    [Table("TKWF_WeChatMessageIdempotency")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatMessageIdempotency_Key", nameof(IdempotencyKey), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class WeChatMessageIdempotencyEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>规范化幂等键（ChannelId+MsgId+FromUserName+CreateTime 规范化拼接——唯一索引，防微信重推）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(300)]
        public string IdempotencyKey { get; set; } = "";

        /// <summary>所属 channel 实例 id（幂等键组成部分之一）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>微信消息 id（MsgId——可空，事件类消息无 MsgId，幂等键相应规范化）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(100)]
        public string? MsgId { get; set; }

        /// <summary>消息发送者 OpenId（FromUserName——幂等键组成部分之一）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(200)]
        public string FromUserName { get; set; } = "";

        /// <summary>微信原始消息时间（原样留存——非 UTC 转换，微信时间戳语义保留）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTime CreateTime { get; set; }

        /// <summary>首次见到该幂等键时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

        /// <summary>最后状态（int——1=Pending 处理中 / 2=Forwarded 已转发 / 3=Skipped 已跳过）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public int LastStatus { get; set; }

        /// <summary>关联转发日志 id（<see cref="MessageForwardLogEntity"/>——可空，Skipped 未转发时为空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public long? ForwardLogId { get; set; }

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
