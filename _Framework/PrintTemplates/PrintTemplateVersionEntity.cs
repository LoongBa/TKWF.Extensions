using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 打印模板版本表实体——关联模板（TemplateId）+ 版本号（SemVer 字符串）+ 正文 + 状态。
    /// <para>TemplateId + Version 唯一（并发发布防冲突——败者显式异常）。</para>
    /// </summary>
    [Table("PrintTemplateVersion")]
    [FreeSql.DataAnnotations.Index("IX_ptv_template_version", nameof(TemplateId) + "," + nameof(Version), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("IX_ptv_template_status", nameof(TemplateId) + "," + nameof(Status), IsUnique = false)]
    [FreeSql.DataAnnotations.Index("IX_ptv_template", nameof(TemplateId), IsUnique = false)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class PrintTemplateVersionEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属模板 ID（外键）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public long TemplateId { get; set; }

        /// <summary>版本号（SemVer 字符串，如 "1.0.0"；Draft 用 "1.{next}.0-draft"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(32)]
        public string Version { get; set; } = "";

        /// <summary>模板正文（Scriban 语法）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public string Content { get; set; } = "";

        /// <summary>版本状态（Draft / Active / Archived）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public PrintTemplateVersionStatus Status { get; set; } = PrintTemplateVersionStatus.Draft;

        /// <summary>版本说明。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(512)]
        public string? Description { get; set; }

        /// <summary>发布时间（Active 时非空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7, IsNullable = true)]
        public DateTimeOffset? PublishedAt { get; set; }

        /// <summary>发布人（Active 时非空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8, IsNullable = true)]
        [MaxLength(128)]
        public string? PublishedBy { get; set; }

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9, ServerTime = System.DateTimeKind.Local, CanUpdate = false)]
        public DateTimeOffset CreateTime { get; set; }
    }
}
