using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Federation
{
    /// <summary>
    /// 关注关系状态聚合根（I2 渠道关注关系域——「当前是否关注」唯一权威；单用户单渠道一行）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4；对齐
    /// <see cref="SsoChannelRegistryEntity"/> 体例）。</para>
    /// <para>排序守卫（方案 §4.3 oracle6 C4——<b>不可用元组 &gt; 运算符</b>，且该形式无法翻译 SQL，必须两段比较）：
    /// <code>
    /// newer = evt.OccurredAt &gt; state.LastEventAt
    ///      || (evt.OccurredAt == state.LastEventAt &amp;&amp; evt.ReceivedAt &gt; state.LastReceivedAt);
    /// </code>
    /// 仅当 <c>newer</c> 时覆盖状态字段；事件行无论是否 newer 都落库（事实不可丢）。两处实现必须同型：
    /// ① <c>FollowStateService.RecordAsync</c> 的 LINQ <c>Where</c>（条件更新 <c>EntityUpdateWhereAsync</c>）；
    /// ② 更新前的内存判定（返回 0 行时判定「被排序守卫挡下」而非「行不存在」）。</para>
    /// <para>聚合根语义（§2.2/R8）：状态是<b>领域事实</b>（非派生投影）——事件流重放是重建手段非定义，Rebuild 只用于
    /// 数据修复；与 Iteration 1 D9「禁派生投影列」判据区分（见 ADR）。</para>
    /// </summary>
    [Table("TKWF_FollowState")]
    [FreeSql.DataAnnotations.Index("TKWFIX_FollowState_Channel_External", "ChannelId,ExternalUserId", IsUnique = true)]
    [FreeSql.DataAnnotations.Index("TKWFIX_FollowState_UnionId", "UnionId")]
    [FreeSql.DataAnnotations.Index("TKWFIX_FollowState_External", "ExternalUserId")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class FollowStateEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>渠道实例（公众号 id——对齐 ISsoChannel.ChannelId）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(64)]
        public string ChannelId { get; set; } = "";

        /// <summary>渠道身份标识（openid / userid——唯一键 (ChannelId, ExternalUserId)）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string ExternalUserId { get; set; } = "";

        /// <summary>跨渠道关联 id（微信 UnionId——最近一次已知，可空；跨渠道关联查询索引）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string? UnionId { get; set; }

        /// <summary>当前关注状态（true = 关注）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public bool IsFollowing { get; set; }

        /// <summary>最近一次关注时间（UTC，可空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTime? SubscribedAt { get; set; }

        /// <summary>最近一次取关时间（UTC，可空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime? UnsubscribedAt { get; set; }

        /// <summary>最近事件类型（follow / unfollow 等——诊断/重建口径）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(32)]
        public string LastEventType { get; set; } = "";

        /// <summary>最近场景值（扫码 scene / 菜单 key——可空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        [MaxLength(256)]
        public string? LastEventKey { get; set; }

        /// <summary>最近来源（R5——对齐 WeChatOperations 统计维度）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(64)]
        public string? LastSource { get; set; }

        /// <summary>最近事件时间（UTC——排序守卫主判据；旧事件不覆盖新状态）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime LastEventAt { get; set; }

        /// <summary>同秒并列时的次判据（服务端接收时间——UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime LastReceivedAt { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 14)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}