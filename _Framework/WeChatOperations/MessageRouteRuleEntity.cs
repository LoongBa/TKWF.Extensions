using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 消息路由规则实体（表 <c>TKWF_WeChatMessageRouteRule</c>）——网关消息路由规则（V0.2.0 增网关模块）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。</para>
    /// <para><see cref="MsgType"/>（string）：微信消息类型（text/image/voice/video/shortvideo/location/link/event）——
    /// <b>逗号分隔多值</b>（如 "text,image" 命中任一类型；空 = 全类型通配；运行库 matcher 按 <c>Split(',')</c> 任一相等判定）。
    /// <see cref="Event"/>（string）：仅 <see cref="MsgType"/> 含 event 时参与（subscribe/unsubscribe/SCAN/CLICK/LOCATION），空 = 全事件。</para>
    /// <para><see cref="MatchMode"/>（int）：0=None 无匹配（纯类型/事件路由）/ 1=Exact 完全匹配 / 2=Contains 包含匹配。
    /// <see cref="TriggerKey"/>：匹配键（MatchMode=None 时恒空）。</para>
    /// <para><see cref="ForwardMode"/>（int）：1=LocalOnly 仅本地回复 / 2=AsyncCopy 异步复制 / 3=SyncProxy 同步代理 /
    /// 4=PreprocessSync 预处理后同步。<see cref="EndpointId"/>：下游端点引用——LocalOnly 恒空（仅本地回复不转发下游）。</para>
    /// <para>规则读一律按 <see cref="ChannelId"/> 过滤（多公众号数据隔离），同 channel 内按 <see cref="SortOrder"/> 升序尝试。</para>
    /// </summary>
    [Table("TKWF_WeChatMessageRouteRule")]
    [FreeSql.DataAnnotations.Index("TKWFIX_WeChatMessageRouteRule_Channel_Type_Event", nameof(ChannelId) + "," + nameof(MsgType) + "," + nameof(Event))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class MessageRouteRuleEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属 channel 实例 id（多公众号数据隔离——规则读一律按 ChannelId 过滤）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string ChannelId { get; set; } = "";

        /// <summary>规则显示名（DMP-Lite 需求 5.5——每条规则含名称，网关规则列表展示）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        /// <summary>微信消息类型（text/image/voice/video/shortvideo/location/link/event）——<b>逗号分隔多值</b>
        /// （如 "text,image" 命中任一类型；空 = 全类型通配；值 trim + OrdinalIgnoreCase 由运行库 matcher 判定）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(50)]
        public string? MsgType { get; set; }

        /// <summary>事件类型（仅 MsgType=event 参与：subscribe/unsubscribe/SCAN/CLICK/LOCATION）——空 = 全事件。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(50)]
        public string? Event { get; set; }

        /// <summary>匹配模式（int——0=None 无匹配（纯类型/事件路由）/ 1=Exact 完全匹配 / 2=Contains 包含匹配）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public int MatchMode { get; set; }

        /// <summary>匹配键（MatchMode=None 时恒空——纯类型/事件路由不参与键匹配）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(200)]
        public string? TriggerKey { get; set; }

        /// <summary>转发模式（int——1=LocalOnly 仅本地回复 / 2=AsyncCopy 异步复制 / 3=SyncProxy 同步代理 / 4=PreprocessSync 预处理后同步）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public int ForwardMode { get; set; }

        /// <summary>下游端点引用（<see cref="DownstreamEndpointEntity"/>——LocalOnly 恒空；其余模式指向转发目标）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public long? EndpointId { get; set; }

        /// <summary>启用（默认 true；禁用 → 规则跳过不参与路由）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>排序（同 channel 内小在前——多规则按序尝试，首个命中生效）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public int SortOrder { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
