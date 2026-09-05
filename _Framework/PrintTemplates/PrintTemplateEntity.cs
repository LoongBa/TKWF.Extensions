using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 打印模板表实体——定义模板键（如 "Invoice.Standard"）+ 显示名 + 描述。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。
    /// SG1 自动生成 <see cref="TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>
    /// <para>保留 BCL <c>[Table("PrintTemplate")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// </summary>
    [Table("PrintTemplate")]
    [FreeSql.DataAnnotations.Index("IX_pt_key", nameof(Key), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class PrintTemplateEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>模板键（唯一，如 "Invoice.Standard"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string Key { get; set; } = "";

        /// <summary>显示名（如 "标准发票"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(256)]
        public string Name { get; set; } = "";

        /// <summary>描述。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(512)]
        public string? Description { get; set; }

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5, ServerTime = System.DateTimeKind.Local, CanUpdate = false)]
        public DateTimeOffset CreateTime { get; set; }

        /// <summary>更新时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6, ServerTime = System.DateTimeKind.Local)]
        public DateTimeOffset UpdateTime { get; set; }
    }
}
