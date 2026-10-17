using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 交互日志实体（表 <c>TKWF_WeChatInteractionLog</c>）——运维排查刚需（每次回复记录，含触发类型/回复内容/所引素材）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para>TriggerType（int）：1=Subscribe / 2=Keyword / 3=Click / 4=Image / 5=Voice / 6=Video / 7=ShortVideo /
    /// 8=Location / 9=Link / 10=Scan / 11=Default / 12=None（未命中规则）。</para>
    /// <para>Oracle P2：<see cref="ReplyMaterialId"/> 可空——追溯回复了哪个素材（全局素材 <see cref="MaterialEntity"/> 引用）。</para>
    /// </summary>
    [Table("TKWF_WeChatInteractionLog")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatInteractionLog_Channel_Time", nameof(ChannelId) + "," + nameof(MsgTime))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class InteractionLogEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属 channel 实例 id。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>触发用户 openid（公众号用户标识）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(100)]
        public string OpenId { get; set; } = "";

        /// <summary>消息时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public DateTime MsgTime { get; set; } = DateTime.UtcNow;

        /// <summary>消息类型（wx XML MsgType 原文：text/event/image/...）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(50)]
        public string MsgType { get; set; } = "";

        /// <summary>请求消息内容（文本——text 正文/事件 EventKey 等）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public string? Content { get; set; }

        /// <summary>触发场景类型（int——同上 TriggerType 值域；回复命中场景）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public int TriggerType { get; set; }

        /// <summary>回复内容（实际下发文本/图文 JSON——null = 未回复）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public string? ReplyContent { get; set; }

    /// <summary>回复所引素材 id（可空——全局素材引用；直接文本回复为 null）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9)]
    public long? ReplyMaterialId { get; set; }

    /// <summary>消息处理耗时（毫秒——V0.3.0 耗时字段，DMP-Lite 5.6 交互日志"耗时 ms"；<see cref="WeChatOperationsReplyHandler"/>
    /// 经 <c>Stopwatch</c> 自 Handler 入口起计时、记录交互日志时止——日志详情展示性能指标）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10)]
    public long? DurationMs { get; set; }

    /// <summary>匹配过程追踪（JSON 数组字符串——V0.3.1 匹配过程逐行推演字段，DMP-Lite 5.6 §4 日志详情"匹配过程"灰框）：
    /// 每次自动回复匹配尝试经 <see cref="WeChatOperationsReplyHandler"/> 逐行记录（规则 id/规则名/场景类型/匹配模式/
    /// 触发键/命中与否/简短原因——如 "TriggerKey==content"/"content contains key"/"无 Default 规则"/"template fallback"），
    /// 供消费方前端逐行渲染"为什么命中/未命中"；仅旁路采集不改动回复决策；null = 未采集（best-effort）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11)]
    public string? MatchTraceJson { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 12)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>更新时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 13)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}