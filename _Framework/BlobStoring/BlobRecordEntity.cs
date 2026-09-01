using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// Blob 记录表实体——存储二进制大对象的元数据（名称、路径、内容类型、大小、标签、上传者等）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。
    /// SG1 自动生成 <see cref="TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>
    /// <para>保留 BCL <c>[Table("BlobRecord")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// </summary>
    [Table("BlobRecord")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class BlobRecordEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>Blob 名称（业务键，如文件名）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(256)]
        public string Name { get; set; } = "";

        /// <summary>存储路径（相对 RootPath 的路径）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(1024)]
        public string Path { get; set; } = "";

        /// <summary>MIME 内容类型（如 image/png）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string? ContentType { get; set; }

        /// <summary>文件大小（字节）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public long Size { get; set; }

        /// <summary>标签（JSON 数组字符串）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public string? Tags { get; set; }

        /// <summary>上传者名称。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(128)]
        public string? UploaderName { get; set; }

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;

        /// <summary>更新时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTimeOffset UpdateTime { get; set; } = DateTimeOffset.Now;
    }
}
