using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Calendar
{
    /// <summary>
    /// 日历实体——多日历隔离（Code 唯一索引，F1）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("Calendar")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// <para>删除语义：物理删除（硬删）——<b>不声明 IsDeleted</b>，DataService 基类 <c>hasSoftDelete:false</c>；
    /// 删除保护（无事件）由 Manager 层强制（D17）。</para>
    /// <para>审计字段（D5）：<c>DateTime</c>（UTC）显式声明（对齐 DataDictionary/Tagging 先例，
    /// FreeSql SQLite 不支持 DateTimeOffset）；不声明 IsDeleted（物理删除）。</para>
    /// </summary>
    [Table("Calendar")]
    [FreeSql.DataAnnotations.Index("UX_Calendar_Code", nameof(Code), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class CalendarEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>日历编码（唯一，如 "Personal" / "Team"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string Code { get; set; } = "";

        /// <summary>日历名称。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string Name { get; set; } = "";

        /// <summary>描述。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true)]
        [MaxLength(512)]
        public string? Description { get; set; }

        /// <summary>颜色（Hex，如 "#3498DB"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5, IsNullable = true)]
        [MaxLength(16)]
        public string? Color { get; set; }

        /// <summary>是否启用（默认 true；GetOccurrencesAsync 默认排除禁用日历的事件，P3/D18）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（UTC，显式声明）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7, CanUpdate = false)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC，显式声明）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
