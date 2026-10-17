using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 消息转发日志实体（表 <c>TKWF_WeChatMessageForwardLog</c>）——网关消息转发流水（一次入站 → 路由决策 → 出站结果全链记录，V0.2.0 增网关模块）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para><see cref="Status"/>（int）：1=Queued 已入队 / 2=Success 成功 / 3=Fail 失败 / 4=Timeout 超时 /
    /// 5=SkippedDuplicate 重复跳过（幂等命中）/ 6=NoRule 无匹配规则。</para>
    /// <para><see cref="ResponseBody"/> 截断 500（出站响应原样截断留存）；<see cref="ErrorMsg"/> 脱敏 1000（异常消息不含明文凭证）。</para>
    /// <para>日志按 <see cref="ChannelId"/> + <see cref="MsgId"/> 检索（非唯一索引——同一消息多路由/重试可多行）。</para>
    /// </summary>
    [Table("TKWF_WeChatMessageForwardLog")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatMessageForwardLog_Channel_Msg", nameof(ChannelId) + "," + nameof(MsgId))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class MessageForwardLogEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属 channel 实例 id（多公众号数据隔离——日志读一律按 ChannelId 过滤）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>微信消息 id（MsgId——可空，事件类消息无 MsgId）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(100)]
        public string? MsgId { get; set; }

        /// <summary>消息发送者 OpenId（FromUserName）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(200)]
        public string FromUserName { get; set; } = "";

        /// <summary>消息类型（text/image/voice/video/shortvideo/location/link/event——路由匹配依据）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(50)]
        public string MessageType { get; set; } = "";

        /// <summary>命中路由规则 id（<see cref="MessageRouteRuleEntity"/>——可空，NoRule 时为空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public long? MatchRuleId { get; set; }

        /// <summary>转发模式（int——1=LocalOnly / 2=AsyncCopy / 3=SyncProxy / 4=PreprocessSync，命中规则快照）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public int ForwardMode { get; set; }

        /// <summary>出站下游端点 id（<see cref="DownstreamEndpointEntity"/>——可空，LocalOnly/NoRule 时为空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public long? DownstreamEndpointId { get; set; }

        /// <summary>出站 HTTP 状态码（可空——未出站或超时无状态码）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public int? StatusCode { get; set; }

        /// <summary>出站响应体（截断 500 字符留存）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(500)]
        public string? ResponseBody { get; set; }

        /// <summary>转发耗时（毫秒——可空，未出站无耗时）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public long? DurationMs { get; set; }

        /// <summary>转发状态（int——1=Queued / 2=Success / 3=Fail / 4=Timeout / 5=SkippedDuplicate / 6=NoRule）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public int Status { get; set; }

        /// <summary>错误信息（脱敏 1000——异常消息不含明文凭证/敏感内容）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        [MaxLength(1000)]
        public string? ErrorMsg { get; set; }

        /// <summary>实际转发时间（UTC——可空，未转发完成时为空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 14)]
        public DateTime? ForwardedAt { get; set; }

        /// <summary>重试次数（失败重试累计）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 15)]
        public int RetryCount { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 16)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 17)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
