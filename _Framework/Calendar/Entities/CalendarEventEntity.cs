using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Calendar
{
    /// <summary>
    /// 日历事件实体——单次或重复事件（归属日历 + UTC 时间 + 全天标记，F2/F3）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("CalendarEvent")]</c>；列映射用 FreeSql <c>[Column]</c>（全限定）。</para>
    /// <para>重复语义（ADR-Calendar C1/C2/P4）：<c>RecurrenceRule</c> 存 <c>RecurrenceRule.ToString()</c> 规范规则串
    /// （可空 = 单次事件；列名存规则文本非 JSON）；<c>RecurrenceEndUtc</c> 为冗余截止（UNTIL 直取 / COUNT 与展开算法
    /// <b>同源</b>推导取最后 occurrence——写入时受上限保护运行展开，偏差构造性消除），加速重复路径范围预筛。</para>
    /// <para>时长语义（C1/P1/P2）：<c>EndUtc</c> 可空（空 = 瞬时事件）；重复事件允许非空 = <b>首 occurrence 时长锚点</b>
    /// （occurrence 时长 = EndUtc - StartUtc，展开时起点 + 偏移）；AllDay 事件 EndUtc 落库 = 次日 00:00 UTC。</para>
    /// <para>删除语义：物理删除（不声明 IsDeleted，hasSoftDelete:false）；occurrence 不物化——展开是查询时计算。</para>
    /// <para>索引（P8）：<c>IX_CalendarEvent_CalendarId_StartUtc</c>（单次/默认路径）+ <c>IX_CalendarEvent_CalendarId_RecurrenceEndUtc</c>（重复路径预筛）。</para>
    /// </summary>
    [Table("CalendarEvent")]
    [FreeSql.DataAnnotations.Index("IX_CalendarEvent_CalendarId_StartUtc", nameof(CalendarId) + "," + nameof(StartUtc))]
    [FreeSql.DataAnnotations.Index("IX_CalendarEvent_CalendarId_RecurrenceEndUtc", nameof(CalendarId) + "," + nameof(RecurrenceEndUtc))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class CalendarEventEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>归属日历 Id（引用守卫：创建/变更时校验日历存在）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public long CalendarId { get; set; }

        /// <summary>事件标题。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(256)]
        public string Title { get; set; } = "";

        /// <summary>描述。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true)]
        [MaxLength(1024)]
        public string? Description { get; set; }

        /// <summary>地点。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5, IsNullable = true)]
        [MaxLength(256)]
        public string? Location { get; set; }

        /// <summary>开始时间（UTC，主时间）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTime StartUtc { get; set; }

        /// <summary>结束时间（UTC，可空 = 瞬时；重复事件 = 首 occurrence 时长锚点）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7, IsNullable = true)]
        public DateTime? EndUtc { get; set; }

        /// <summary>全天标记（UTC 日期语义；EndUtc 落库 = 次日 00:00，P2）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool AllDay { get; set; }

        /// <summary>重复规则规范串（RecurrenceRule.ToString()；可空 = 单次事件）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
        [MaxLength(512)]
        public string? RecurrenceRule { get; set; }

        /// <summary>冗余截止（UNTIL 直取 / COUNT 同源推导 / 无界 null；重复路径范围预筛，C2/P8）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10, IsNullable = true)]
        public DateTime? RecurrenceEndUtc { get; set; }

        /// <summary>创建时间（UTC，显式声明）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11, CanUpdate = false)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC，显式声明）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
