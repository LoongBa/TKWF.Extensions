using System;
using FreeSql.DataAnnotations;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 打印模板版本视图实体（VEntity）——跨表 JOIN <c>PrintTemplateVersion</c> → <c>PrintTemplate</c>，
    /// 携带模板 <c>Key</c>/<c>TemplateName</c>，按 Key 单查询返回版本。
    /// <para>V0.2.0：替代两步查询（先 GetByKeyAsync 取 TemplateId 再查版本）——JOIN 下推 DB，单查询完成，
    /// 并补足当前返回面缺失的模板名（免第 3 次往返）。RenderAsync 从 2 次往返降为 1 次。
    /// VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。</para>
    /// <para>⚠️ 敏感视图（C4）：含模板正文 <c>Content</c>（商业资产）——<c>ExposeGraphqlQuery = false</c>，
    /// 数据访问统一经 <see cref="ITemplateManager"/> 门面（门面内已有状态机/权限语义）。</para>
    /// </summary>
    [Table(Name = "vw_PrintTemplateVersionView", DisableSyncStructure = true)]
    [DomainGenerateCode(IsView = true,
        ViewSql = @"CREATE OR REPLACE VIEW ""vw_PrintTemplateVersionView"" AS
SELECT v.""Id"", v.""TemplateId"", t.""Key"", t.""Name"" AS ""TemplateName"", t.""Description"" AS ""TemplateDescription"",
       v.""Version"", v.""Content"", v.""Status"", v.""Description"", v.""PublishedAt"", v.""PublishedBy"", v.""CreateTime""
FROM ""PrintTemplateVersion"" v
INNER JOIN ""PrintTemplate"" t ON v.""TemplateId"" = t.""Id""",
        ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""vw_PrintTemplateVersionView"" AS
SELECT v.""Id"", v.""TemplateId"", t.""Key"", t.""Name"" AS ""TemplateName"", t.""Description"" AS ""TemplateDescription"",
       v.""Version"", v.""Content"", v.""Status"", v.""Description"",
       NULL AS ""PublishedAt"", NULL AS ""PublishedBy"", NULL AS ""CreateTime""
FROM ""PrintTemplateVersion"" v
INNER JOIN ""PrintTemplate"" t ON v.""TemplateId"" = t.""Id""",
        ExposeGraphqlQuery = false,   // ⚠️ 敏感视图显式关闭（C4，含模板正文 Content 商业资产）
        DefaultPageSize = 50)]
    public partial class PrintTemplateVersionView
    {
        /// <summary>主键——透传 Version.Id（PK 透传，多对一 JOIN 行数不变、Id 唯一稳定）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>所属模板 ID（透传 Version.TemplateId）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public long TemplateId { get; set; }

        /// <summary>模板键（核心收益——透传 Template.Key，外层过滤键，替代第 1 步 GetByKeyAsync）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public string Key { get; set; } = "";

        /// <summary>模板显示名（核心收益——当前返回面缺失，透传 Template.Name）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public string TemplateName { get; set; } = "";

        /// <summary>模板描述（透传 Template.Description，可空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public string? TemplateDescription { get; set; }

        /// <summary>版本号（SemVer 字符串，如 "1.0.0"；Draft 用 "1.{next}.0-draft"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public string Version { get; set; } = "";

        /// <summary>模板正文（Scriban 语法，渲染必需——大字段，敏感商业资产）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public string Content { get; set; } = "";

        /// <summary>版本状态（Draft / Active / Archived）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public PrintTemplateVersionStatus Status { get; set; } = PrintTemplateVersionStatus.Draft;

        /// <summary>版本说明（透传 Version.Description，可空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public string? Description { get; set; }

        /// <summary>发布时间（Active 时非空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10, IsNullable = true)]
        public DateTimeOffset? PublishedAt { get; set; }

        /// <summary>发布人（Active 时非空）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11, IsNullable = true)]
        public string? PublishedBy { get; set; }

        /// <summary>创建时间（透传 Version.CreateTime）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12, CanUpdate = false)]
        public DateTimeOffset CreateTime { get; set; }
    }
}